namespace HotStatus
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;
    using Microsoft.VisualStudio.Text;
    using Microsoft.VisualStudio.Text.Editor;
    using Microsoft.VisualStudio.Text.Tagging;
    using Microsoft.VisualStudio.Text.Adornments;
    using Microsoft.VisualStudio.Text.Classification;
    using Microsoft.VisualStudio.Language.StandardClassification;
    using Microsoft.VisualStudio.Language.Intellisense;
    using Microsoft.VisualStudio.Shell;
    using Microsoft.VisualStudio.Shell.TableControl;
    using Microsoft.VisualStudio.Shell.TableManager;
    using System.Threading;
    using System.Threading.Tasks;
    using Task = System.Threading.Tasks.Task;

    internal sealed class ErrorStatusTracker
    {
        private readonly IWpfTextView textView;
        private readonly ErrorStatusTextViewCreationListener textCreationListener;
        private readonly ITagAggregator<IErrorTag> errorTagAggregator;
        private readonly ITagAggregator<IErrorTag> bufferErrorTagAggregator;
        private readonly IAsyncQuickInfoBroker quickInfoBroker;
        private readonly IClassifier classifier;
        private HotStatusOptions optionsPage;

        public ErrorStatusTracker(IWpfTextView textView, IAsyncQuickInfoBroker quickInfoBroker, 
            IClassifierAggregatorService classifierAggregatorService, ErrorStatusTextViewCreationListener textCreationListener)
        {
            this.textView = textView;
            this.quickInfoBroker = quickInfoBroker;
            this.textCreationListener = textCreationListener;

            // Set the classifier based on the textView
            this.classifier = classifierAggregatorService.GetClassifier(textView.TextBuffer);

            // Set the event listeners
            // - BatchedTagsChanged
            // Different hosts register their squiggle/diagnostics tagger at different scopes:
            // C#/JS only expose it at the VIEW level (IViewTaggerProvider), while SQL only
            // exposes it at the BUFFER level (classic ITaggerProvider) - confirmed empirically,
            // view is not a strict superset of buffer here. Query both; the view-level one is
            // tried first in UpdateStatusBarInfoAsync, with the buffer-level one as a fallback.
            this.errorTagAggregator = textCreationListener.ViewTagAggregatorFactoryService.CreateTagAggregator<IErrorTag>(textView);
            this.errorTagAggregator.BatchedTagsChanged += this.OnBatchedTagsChanged;
            this.bufferErrorTagAggregator = textCreationListener.TagAggregatorFactoryService.CreateTagAggregator<IErrorTag>(textView.TextBuffer);
            this.bufferErrorTagAggregator.BatchedTagsChanged += this.OnBatchedTagsChanged;
            // - CaretPositionChanged
            textView.Closed += OnTextViewClosed;
            textView.Caret.PositionChanged += this.OnCaretPositionChanged;
            // - GotKeyboardFocus
            //textView.VisualElement.GotKeyboardFocus += this.OnGotKeyboardFocus;
        }

        private void OnBatchedTagsChanged(object sender, BatchedTagsChangedEventArgs e) => this.UpdateStatusBarInfoAsync().ConfigureAwait(true);

        private void OnCaretPositionChanged(object sender, CaretPositionChangedEventArgs e) => this.UpdateStatusBarInfoAsync().ConfigureAwait(true);

        //private void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => this.UpdateStatusBarInfo();

        private HotStatusOptions Options
        {
            get
            {
                if (optionsPage == null)
                {
                    optionsPage = HotStatusOptions.Instance;
                }
                return optionsPage;
            }
        }

        private bool ShouldShowErrorInfo { get { return HotStatusOptions.Instance.ShowErrorInfo; } }
        private bool ShouldShowSymbolInfo { get { return HotStatusOptions.Instance.ShowSymbolInfo; } }

        private async Task UpdateStatusBarInfoAsync()
        {
            // Fail out early if the user flags are disabled
            if (!(ShouldShowErrorInfo || ShouldShowSymbolInfo)) return;

            // Algorithm for updating status bar text:
            // 1. If there are error tags, show the highest priority error
            // 2. Otherwise, show any current symbol info
            // 3. Otherwise, clear the status bar

            SnapshotPoint caretBufferPosn = this.textView.Caret.Position.BufferPosition;

            // Option 1: Get all error tags that intersect with the caret.
            if (ShouldShowErrorInfo)
            {
                SnapshotSpan currentSnapshotSpan = new SnapshotSpan(caretBufferPosn, 0);
                var errorTagList = this.errorTagAggregator.GetTags(currentSnapshotSpan).ToList();
                if (errorTagList.Count == 0)
                {
                    // Fall back to the buffer-level aggregator for hosts (e.g. SQL) that register
                    // their diagnostics tagger there instead of at the view level.
                    errorTagList = this.bufferErrorTagAggregator.GetTags(currentSnapshotSpan).ToList();
                }
                if (errorTagList.Count > 0)
                {
                    // Error tags exist at this location
                    await ShowErrorTagInfoAsync(errorTagList, caretBufferPosn);
                    return;
                }
            }

            // Option 2: Show current symbol info (ie. method signature, parameter info, variable type)
            if (ShouldShowSymbolInfo)
            {
                int caretPosnInt = caretBufferPosn.Position;
                ITrackingPoint trackingPoint = caretBufferPosn.Snapshot.CreateTrackingPoint(caretPosnInt, PointTrackingMode.Positive);

                // Fix for Issue #4 - Error noise when clicking on C++ comment.
                // Problem: Microsoft.VisualStudio.Language.Intellisense.GetQuickInfoItemsAsync sounds an error when calling this on a C++ comment.
                // Solution: Don't call GetQuickInfoItemsAsync when the caret is on a comment. Just clear the StatusBar instead.
                // If this is a comment, clear the status bar text and exit
                if (IsCaretOnAComment())
                {
                    ClearStatusBarText();
                    return;
                }

                string quickInfoText = await GetQuickInfoTextAsync(caretBufferPosn, trackingPoint);
                if (quickInfoText != null)
                {
                    UpdateStatusBarText(quickInfoText);
                    return;
                }
            }

            // Option 3: No info to display
            ClearStatusBarText();
        }

        private bool IsCaretOnAComment()
        {
            // Check a one-char span starting from the caret position
            SnapshotSpan spanToCheck = new SnapshotSpan(textView.Caret.Position.BufferPosition, 1);

            // Check if the span is on a comment
            IList<ClassificationSpan> classificationSpans = classifier.GetClassificationSpans(spanToCheck);
            foreach (var classification in classificationSpans)
            {
                var name = classification.ClassificationType.Classification.ToLower();
                // If any of the classifications are a comment - then the caret is on a comment. Return true!
                if (name.Contains(PredefinedClassificationTypeNames.Comment))
                {
                    return true;
                }
            }

            // No comment found - return false
            return false;
        }

        private string GetTextFromContainer(ContainerElement containerWithImageAndText)
        {
            if (containerWithImageAndText?.Elements == null) return null;

            List<object> elemList = containerWithImageAndText.Elements.ToList();

            StringBuilder combinedText = new StringBuilder();

            if (elemList[1] is ClassifiedTextElement classifiedText)
            {
                IEnumerable<ClassifiedTextRun> runs = classifiedText.Runs;

                // TODO: This can probably be written as a single line statement
                foreach (var run in runs)
                {
                    combinedText.Append(run.Text);
                }

                string returnText = combinedText.ToString().Trim();

                return returnText;
            }

            return null;
        }

        private ContainerElement GetContainerElementWithImageAndText(ContainerElement containerElem)
        {
            if (containerElem?.Elements == null) return null;

            List<object> elems = containerElem.Elements.ToList();
            
            object firstElem = elems[0];

            // Is this another container?
            if (firstElem is ContainerElement nextContainerElem) {
                return GetContainerElementWithImageAndText(nextContainerElem);
            } 

            // Check if the first element is an image - If so, the second elem contains the text we want
            if (firstElem is ImageElement && elems[1] is ClassifiedTextElement)
            {
                // Return this element
                return containerElem;
            }

            return null;
        }

        private async Task<string> GetQuickInfoTextAsync(SnapshotPoint bufferPosition, ITrackingPoint trackingPoint = null)
        {
            if (trackingPoint == null)
            {
                trackingPoint = bufferPosition.Snapshot.CreateTrackingPoint(bufferPosition.Position, PointTrackingMode.Positive);
            }

            CancellationToken cancellationToken = new CancellationToken();
            QuickInfoItemsCollection info = await quickInfoBroker.GetQuickInfoItemsAsync(textView, trackingPoint, cancellationToken);
            if (info == null) return null;

            List<object> itemsList = info.Items.ToList();
            if (itemsList.Count == 0) return null;

            if (itemsList[0] is ContainerElement containerElem)
            {
                ContainerElement containerWithImageAndText = GetContainerElementWithImageAndText(containerElem);
                return GetTextFromContainer(containerWithImageAndText);
            }

            return null;
        }

        private async Task ShowErrorTagInfoAsync(List<IMappingTagSpan<IErrorTag>> errorTagList, SnapshotPoint caretBufferPosn)
        {
            // Optimisation: ErrorTags list is usually empty (or one). List of known error types is 6+ items.
            // Therefore, avoid iterating through the error type list where possible.
            // Convert the enum to list so we can easily see if it's empty or one.
            // Only where there are more than one ErrorTag do we need to sort by priority.
            // If more than one error tag. Show highest priority error.
            IMappingTagSpan<IErrorTag> mappingTagSpan = (errorTagList.Count > 1) ? GetHighestPriorityErrorTag(errorTagList) : errorTagList[0];

            await this.UpdateStatusBarFromErrorTagAsync(mappingTagSpan, caretBufferPosn);
        }

        private IMappingTagSpan<IErrorTag> GetHighestPriorityErrorTag(List<IMappingTagSpan<IErrorTag>> mappingTagSpans)
        {
            // Get first, highest priority error tag.
            return this.textCreationListener.OrderedErrorTypeDefinitions
                .Select(errorTypeDefinition => mappingTagSpans.FirstOrDefault(tag =>
                    string.Equals(tag.Tag.ErrorType, errorTypeDefinition.Metadata.Name,
                        StringComparison.OrdinalIgnoreCase)))
                .FirstOrDefault(firstMatchingTag => firstMatchingTag != null);
        }

        private async Task UpdateStatusBarFromErrorTagAsync(IMappingTagSpan<IErrorTag> mappingTagSpan, SnapshotPoint caretBufferPosn)
        {
            // Extract the message from the tool tip content (Note: Might return null)
            string errorTagContent = GetTextFromTagToolTip(mappingTagSpan);

            if (string.IsNullOrWhiteSpace(errorTagContent))
            {
                // Some hosts (e.g. SQL) populate the squiggle but leave ErrorTag.ToolTipContent
                // empty; the same message is often still available via the QuickInfo hover
                // source for this caret position, so try that before giving up on showing
                // anything at all.
                errorTagContent = await GetQuickInfoTextAsync(caretBufferPosn);
            }

            if (string.IsNullOrWhiteSpace(errorTagContent))
            {
                // Last resort: some hosts (SQL's syntax-error squiggles, at least) populate
                // neither ToolTipContent nor the async QuickInfo broker. We already know exactly
                // which diagnostic this is from the tag's own mapped span, so look it up in the
                // Error List by matching that precise position - this isn't a guess the way an
                // Error-List-only approach would be, since the tag already confirmed the caret is
                // on this specific error.
                errorTagContent = GetErrorMessageFromErrorListForTag(mappingTagSpan);
            }

            // Update the status bar
            UpdateStatusBarText(errorTagContent);
        }

        private string GetErrorMessageFromErrorListForTag(IMappingTagSpan<IErrorTag> mappingTagSpan)
        {
            try
            {
                NormalizedSnapshotSpanCollection spans = mappingTagSpan.Span.GetSpans(this.textView.TextBuffer);
                if (spans.Count == 0) return null;
                SnapshotPoint tagStart = spans[0].Start;

                IErrorList errorList = this.textCreationListener.ErrorListService;
                IWpfTableControl tableControl = errorList?.TableControl;
                if (tableControl == null) return null;

                if (!this.textView.TextBuffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument textDocument)
                    || string.IsNullOrEmpty(textDocument.FilePath))
                {
                    return null;
                }

                ITextSnapshotLine tagLine = tagStart.GetContainingLine();
                int tagLineZeroBased = tagLine.LineNumber;
                int tagColumnZeroBased = tagStart.Position - tagLine.Start.Position;

                foreach (ITableEntryHandle entry in tableControl.Entries)
                {
                    if (!entry.TryGetValue(StandardTableKeyNames.DocumentName, out object documentNameObj)
                        || !(documentNameObj is string documentName)
                        || !string.Equals(documentName, textDocument.FilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!entry.TryGetValue(StandardTableKeyNames.Line, out object lineObj)
                        || !(lineObj is int line)
                        || line != tagLineZeroBased)
                    {
                        continue;
                    }

                    // Match the exact start column reported by the tag itself, rather than
                    // guessing by proximity - we already know this is the right diagnostic.
                    if (entry.TryGetValue(StandardTableKeyNames.Column, out object columnObj)
                        && columnObj is int column
                        && column != tagColumnZeroBased)
                    {
                        continue;
                    }

                    if (entry.TryGetValue(StandardTableKeyNames.Text, out object textObj)
                        && textObj is string message
                        && !string.IsNullOrWhiteSpace(message))
                    {
                        return message.Trim();
                    }
                }

                return null;
            }
            catch (Exception)
            {
                // Same policy as GetTextFromTagToolTip: don't let a lookup failure here be noisy;
                // this can run on essentially every caret move.
                return null;
            }
        }

        private void UpdateStatusBarText(string newText)
        {
            if (string.IsNullOrWhiteSpace(newText))
            {
                // Handle the case of a Suggestion tag with no tooltip content
                ClearStatusBarText();
            }
            else
            {
                SetStatusBarText(newText);
            }
            // Always update the Last Error Text - even if it is null
            this.textCreationListener.LastStatusBarText = newText;
        }

        private static string GetTextFromTagToolTip(IMappingTagSpan<IErrorTag> mappingTagSpan)
        {
            // Note: There are too many things that could return NullReferenceException here
            // so capturing any exceptions to keep it clean at this point.
            try
            {
                var toolTipContent = (ContainerElement)mappingTagSpan.Tag.ToolTipContent;
                // Note: There might be no ToolTipContent associated with this ErrorTag
                if (toolTipContent == null) return null;

                var textRuns = ((ClassifiedTextElement)toolTipContent.Elements.ElementAt(0)).Runs;
                return ExtractMessageFromTextRuns(textRuns);
            } catch (Exception)
            {
                // Note: Deliberately not sending diagnostics to debug because this action could occur too frequently (ie. every keystroke)
                //System.Diagnostics.Debug.WriteLine("Exception occurred attempting to get error text. Message: " + e.Message);
                return null;
            }
        }

        private static string ExtractMessageFromTextRuns(IEnumerable<ClassifiedTextRun> textRuns)
        {
            var combinedText = new StringBuilder();

            // If there are exactly four (4) textRuns, assume the format "CODE: Message"
            if (textRuns.ToList().Count.Equals(4))
            {
                // Take the 4th item only. (Zero-based index) [Code][:][ ][Message]
                var textRun = textRuns.ElementAt(3);    // This should be the "Message" part of the Runs
                combinedText.Append(textRun.Text);
            }
            // Otherwise, append all textRuns for one message
            else
            {
                // TODO: This can probably be written as a single line statement
                foreach (var run in textRuns)
                {
                    combinedText.Append(run.Text);
                }
            }

            // Return a trimmed string
            return combinedText.ToString().Trim();
        }

        private void SetStatusBarText(string textToDisplay)
        {
            var statusBar = this.textCreationListener.StatusBarService;

            // The status bar can be silently overwritten by other VS operations (background
            // indexing, Copilot status, etc.) unless we freeze it after writing. Unfreeze first
            // in case a previous call left it frozen, so our new text can actually be applied.
            Marshal.ThrowExceptionForHR(statusBar.IsFrozen(out int frozen));
            if (frozen != 0)
            {
                Marshal.ThrowExceptionForHR(statusBar.FreezeOutput(0));
            }

            // Don't set the status bar text if it's already set.
            // Note: Costs a GetText operation. Is this faster than SetText?
            Marshal.ThrowExceptionForHR(statusBar.GetText(out string currentStatusBarText));
            if (!currentStatusBarText.Equals(textToDisplay))
            {
                Marshal.ThrowExceptionForHR(statusBar.SetText(textToDisplay));
            }

            // Freeze the status bar so our text sticks until we explicitly change it again.
            Marshal.ThrowExceptionForHR(statusBar.FreezeOutput(1));
            this.textCreationListener.LastStatusBarText = textToDisplay;
        }

        private void ClearStatusBarText()
        {
            // Don't bother clearing the status bar if we didn't set anything
            if (string.IsNullOrEmpty(this.textCreationListener.LastStatusBarText)) return;

            var statusBar = this.textCreationListener.StatusBarService;

            // Unfreeze before checking/clearing - we may have frozen it ourselves in SetStatusBarText.
            Marshal.ThrowExceptionForHR(statusBar.IsFrozen(out int frozen));
            if (frozen != 0)
            {
                Marshal.ThrowExceptionForHR(statusBar.FreezeOutput(0));
            }

            // Don't clear the status bar if there's nothing in it or if it's not the last error text
            Marshal.ThrowExceptionForHR(statusBar.GetText(out string currentStatusBarText));
            if (string.IsNullOrEmpty(currentStatusBarText) ||
                !string.Equals(currentStatusBarText, this.textCreationListener.LastStatusBarText)) return;

            // The text in the status bar is the text last set. Can safely clear it.
            Marshal.ThrowExceptionForHR(statusBar.Clear());
            this.textCreationListener.LastStatusBarText = null;
        }

        private void OnTextViewClosed(object sender, System.EventArgs e)
        {
            this.errorTagAggregator.BatchedTagsChanged -= this.OnBatchedTagsChanged;
            this.errorTagAggregator.Dispose();
            this.bufferErrorTagAggregator.BatchedTagsChanged -= this.OnBatchedTagsChanged;
            this.bufferErrorTagAggregator.Dispose();

            this.textView.Closed -= this.OnTextViewClosed;
            this.textView.Caret.PositionChanged -= this.OnCaretPositionChanged;
            //textView.VisualElement.GotKeyboardFocus -= this.OnGotKeyboardFocus;
        }
    }
}

