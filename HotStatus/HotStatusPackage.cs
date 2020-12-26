using Microsoft.VisualStudio.Shell;

namespace HotStatus
{
    /// <remarks>
    /// Note: This Package is implemented only because it seems impossible to build an extension without a Package.
    /// The Package will not be loaded into VS unless referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
    /// </remarks>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    // InstalledProductRegistration is the information that appears in Help->About (Name, Description, Version)
    [InstalledProductRegistration("HotStatus Extension",
        "HotStatus Visual Studio Extension by Justin Clareburt. Shows error messages (and warning and info) on the status bar when the caret (keyboard cursor) is on an error marker (ie. squiggle)", 
        "1.0.2", IconResourceID = 400)]
    public sealed class HotStatusPackage : AsyncPackage { }
}
