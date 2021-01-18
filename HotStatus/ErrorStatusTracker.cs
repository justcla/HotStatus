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
    using Microsoft.VisualStudio.Language.Intellisense;
    using System.Threading;
    using System.Threading.Tasks;

    internal sealed class ErrorStatusTracker
    {
        private readonly IWpfTextView textView;
        private readonly ErrorStatusTextViewCreationListener factory;
        private readonly ITagAggregator<IErrorTag> errorTagAggregator;
        private readonly IAsyncQuickInfoBroker quickInfoBroker;
        private HotStatusOptions optionsPage;

        public ErrorStatusTracker(IWpfTextView textView, IAsyncQuickInfoBroker quickInfoBroker, ErrorStatusTextViewCreationListener factory)
        {
            this.textView = textView;
            this.quickInfoBroker = quickInfoBroker;
            this.factory = factory;

            // Set the event listeners
            // - BatchedTagsChanged
            this.errorTagAggregator = factory.TagAggregatorFactoryService.CreateTagAggregator<IErrorTag>(textView.TextBuffer);
            this.errorTagAggregator.BatchedTagsChanged += this.OnBatchedTagsChanged;
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

            var caretBufferPosn = this.textView.Caret.Position.BufferPosition;

            // Option 1: Get all error tags that intersect with the caret.
            if (ShouldShowErrorInfo)
            {
                SnapshotSpan currentSnapshotSpan = new SnapshotSpan(caretBufferPosn, 0);
                var errorTagList = this.errorTagAggregator.GetTags(currentSnapshotSpan).ToList();
                if (errorTagList.Count > 0)
                {
                    // Error tags exist at this location
                    ShowErrorTagInfo(errorTagList);
                    return;
                }
            }

            // Option 2: Show current symbol info (ie. method signature, parameter info, variable type)
            if (ShouldShowSymbolInfo)
            {
                int caretPosnInt = caretBufferPosn.Position;
                ITrackingPoint trackingPoint = caretBufferPosn.Snapshot.CreateTrackingPoint(caretPosnInt, PointTrackingMode.Positive);
                CancellationToken cancellationToken = new CancellationToken();
                // TODO: Run asynchronously
                Task<QuickInfoItemsCollection> task = quickInfoBroker.GetQuickInfoItemsAsync(textView, trackingPoint, cancellationToken);
                QuickInfoItemsCollection info = await task;
                if (info != null)
                {
                    IEnumerable<object> infoItems = info.Items;
                    List<object> itemsList = infoItems.ToList();
                    if (itemsList[0] is ContainerElement containerElem)
                    {
                        ContainerElement containerWithImageAndText = GetContainerElementWithImageAndText(containerElem);
                        string rawText = GetTextFromContainer(containerWithImageAndText);
                        UpdateStatusBarText(rawText);
                        return;
                    }
                }
            }

            // Option 3: No info to display
            ClearStatusBarText();
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

        private void ShowErrorTagInfo(List<IMappingTagSpan<IErrorTag>> errorTagList)
        {
            // Optimisation: ErrorTags list is usually empty (or one). List of known error types is 6+ items.
            // Therefore, avoid iterating through the error type list where possible.
            // Convert the enum to list so we can easily see if it's empty or one.
            // Only where there are more than one ErrorTag do we need to sort by priority.
            // If more than one error tag. Show highest priority error.
            IMappingTagSpan<IErrorTag> mappingTagSpan = (errorTagList.Count > 1) ? GetHighestPriorityErrorTag(errorTagList) : errorTagList[0];

            this.UpdateStatusBarFromErrorTag(mappingTagSpan);
        }

        private IMappingTagSpan<IErrorTag> GetHighestPriorityErrorTag(List<IMappingTagSpan<IErrorTag>> mappingTagSpans)
        {
            // Get first, highest priority error tag.
            return this.factory.OrderedErrorTypeDefinitions
                .Select(errorTypeDefinition => mappingTagSpans.FirstOrDefault(tag =>
                    string.Equals(tag.Tag.ErrorType, errorTypeDefinition.Metadata.Name,
                        StringComparison.OrdinalIgnoreCase)))
                .FirstOrDefault(firstMatchingTag => firstMatchingTag != null);
        }

        private void UpdateStatusBarFromErrorTag(IMappingTagSpan<IErrorTag> mappingTagSpan)
        {
            // Extract the message from the tool tip content (Note: Might return null)
            string errorTagContent = GetTextFromTagToolTip(mappingTagSpan);

            // Update the status bar
            UpdateStatusBarText(errorTagContent);
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
            this.factory.LastStatusBarText = newText;
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
            // Don't set the status bar text if it's already set.
            // Note: Costs a GetText operation. Is this faster than SetText?
            Marshal.ThrowExceptionForHR(this.factory.StatusBarService.GetText(out string currentStatusBarText));
            if (currentStatusBarText.Equals(textToDisplay)) return;

            Marshal.ThrowExceptionForHR(this.factory.StatusBarService.SetText(textToDisplay));
            this.factory.LastStatusBarText = textToDisplay;
        }

        private void ClearStatusBarText()
        {
            // Don't bother clearing the status bar if we didn't set anything
            if (string.IsNullOrEmpty(this.factory.LastStatusBarText)) return;

            // Don't clear the status bar if there's nothing in it or if it's not the last error text
            Marshal.ThrowExceptionForHR(this.factory.StatusBarService.GetText(out string currentStatusBarText));
            if (string.IsNullOrEmpty(currentStatusBarText) ||
                !string.Equals(currentStatusBarText, this.factory.LastStatusBarText)) return;

            // The text in the status bar is the text last set. Can safely clear it.
            Marshal.ThrowExceptionForHR(this.factory.StatusBarService.Clear());
            this.factory.LastStatusBarText = null;
        }

        private void OnTextViewClosed(object sender, System.EventArgs e)
        {
            this.errorTagAggregator.BatchedTagsChanged -= this.OnBatchedTagsChanged;
            this.errorTagAggregator.Dispose();

            this.textView.Closed -= this.OnTextViewClosed;
            this.textView.Caret.PositionChanged -= this.OnCaretPositionChanged;
            //textView.VisualElement.GotKeyboardFocus -= this.OnGotKeyboardFocus;
        }
    }
}

