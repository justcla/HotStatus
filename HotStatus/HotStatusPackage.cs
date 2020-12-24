using Microsoft.VisualStudio.Shell;

namespace HotStatus
{
    /// <remarks>
    /// Note: This Package is implemented only because it seems impossible to build an extension without a Package.
    /// The Package will not be loaded into VS unless referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
    /// </remarks>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    public sealed class HotStatusPackage : AsyncPackage { }
}
