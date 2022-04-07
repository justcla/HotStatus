﻿namespace HotStatus
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.Composition;
    using Microsoft.VisualStudio.Language.Intellisense;
    using Microsoft.VisualStudio.Shell;
    using Microsoft.VisualStudio.Shell.Interop;
    using Microsoft.VisualStudio.Text.Adornments;
    using Microsoft.VisualStudio.Text.Classification;
    using Microsoft.VisualStudio.Text.Editor;
    using Microsoft.VisualStudio.Text.Tagging;
    using Microsoft.VisualStudio.Utilities;

    // Bring in ErrorStatus tracker for any view that is 'Analyzable'. A.K.A.: any view that can show squiggles, including peek.
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("any")]
    [TextViewRole(PredefinedTextViewRoles.Analyzable)]
    internal sealed class ErrorStatusTextViewCreationListener : IWpfTextViewCreationListener
    {
        internal readonly IBufferTagAggregatorFactoryService TagAggregatorFactoryService;
        internal readonly IEnumerable<Lazy<ErrorTypeDefinition, IOrderable>> UnorderedErrorTypeDefinition;
        internal readonly SVsServiceProvider ServiceProvider;

        private IEnumerable<Lazy<ErrorTypeDefinition, IOrderable>> orderedErrorTypeDefinitions;
        private IVsStatusbar statusBarService;

        [Import]
        internal IAsyncQuickInfoBroker quickInfoBroker;

        [Import]
        private IClassifierAggregatorService classifierAggregatorService;

        [ImportingConstructor]
        public ErrorStatusTextViewCreationListener(
            IBufferTagAggregatorFactoryService tagAggregatorFactoryService,
            [ImportMany] IEnumerable<Lazy<ErrorTypeDefinition, IOrderable>> unorderedErrorTypeDefinitions,
            SVsServiceProvider serviceProvider)
        {
            this.TagAggregatorFactoryService = tagAggregatorFactoryService
                ?? throw new ArgumentNullException(nameof(tagAggregatorFactoryService));
            this.UnorderedErrorTypeDefinition = unorderedErrorTypeDefinitions
                ?? throw new ArgumentNullException(nameof(unorderedErrorTypeDefinitions));
            this.ServiceProvider = serviceProvider
                ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public void TextViewCreated(IWpfTextView textView)
        {
            // TODO: Fix this.
            new ErrorStatusTracker(textView, quickInfoBroker, classifierAggregatorService, this);
        }

        // Lazily sort all defined ErrorTypes by their precedence.
        internal IEnumerable<Lazy<ErrorTypeDefinition, IOrderable>> OrderedErrorTypeDefinitions => this.orderedErrorTypeDefinitions
            ?? (this.orderedErrorTypeDefinitions = Orderer.Order(this.UnorderedErrorTypeDefinition));

        internal IVsStatusbar StatusBarService => this.statusBarService
            ?? (this.statusBarService = this.ServiceProvider.GetService(typeof(SVsStatusbar)) as IVsStatusbar);

        // Keep track of the last error message added to the status bar so we don't clear other messages.
        internal string LastStatusBarText { get; set; }
    }
}
