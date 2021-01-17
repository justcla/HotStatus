using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Threading;
using Task = System.Threading.Tasks.Task;

namespace HotStatus
{
    /// <remarks>
    /// Note: This Package is implemented only because it seems impossible to build an extension without a Package.
    /// The Package will not be loaded into VS unless referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
    /// </remarks>
    // InstalledProductRegistration is the information that appears in Help->About (Name, Description, Version)
    [InstalledProductRegistration("HotStatus Extension",
        "HotStatus Visual Studio Extension by Justin Clareburt. Displays information on the Status Bar for the item/symbol/error at the current location.",
        "1.1.2", IconResourceID = 400)]
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [ProvideService(typeof(HotStatusPackage), IsAsyncQueryable = true)]
    [ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideOptionPage(typeof(HotStatusOptions), "Hot Settings", "Hot Status", 0, 0, true)]
    public sealed class HotStatusPackage : AsyncPackage
    {
        protected override Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            HotStatusOptions.Instance.LoadSettingsFromStorage();
            return base.InitializeAsync(cancellationToken, progress);
        }
    }
}
