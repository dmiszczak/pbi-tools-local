using System;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Spectre.Console.Cli;

internal class Program
{
    private static int Main(string[] args)
    {
		var app = new CommandApp();

		app.Configure(config =>
		{
            config.AddCommand<DownloadPbiDesktopExeCommand>("download-pbidesktop-exe");
            config.AddCommand<UnbindBundleCommand>("extract-exe");
            config.AddCommand<InstallMsiCommand>("expand-msi");
        });

		return app.Run(args);
    }
}


internal class UnbindBundleCommand : Command<UnbindBundleCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<exePath>")]
        public string ExePath { get; set; }

        [CommandArgument(1, "<targetFolder>")]
        public string TargetFolder { get; set; }

        [CommandOption("--deleteTempFiles"), DefaultValue(true)]
        public bool DeleteTempFiles { get; set; }
    }


    public override int Execute(CommandContext context, Settings settings)
    {
		Directory.CreateDirectory(settings.TargetFolder);

		var unbinder = new Microsoft.Tools.WindowsInstallerXml.Unbinder();
		unbinder.TempFilesLocation = settings.TargetFolder;
		unbinder.Unbind(settings.ExePath, Microsoft.Tools.WindowsInstallerXml.OutputType.Bundle, settings.TargetFolder);
		
		if (settings.DeleteTempFiles) unbinder.DeleteTempFiles();

		return 0;
    }

}

internal class InstallMsiCommand : Command<InstallMsiCommand.Settings>
{
	[DllImport("msi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern UInt32 MsiEnableLog(INSTALLLOGMODE dwLogMode, string szLogFile, INSTALLLOGATTRIBUTES dwLogAttributes);

    [DllImport("msi.dll", SetLastError = true)]
    static extern int MsiSetInternalUI(INSTALLUILEVEL dwUILevel, ref IntPtr phWnd);

	public enum INSTALLUILEVEL
	{
		INSTALLUILEVEL_NOCHANGE = 0,    // UI level is unchanged
		INSTALLUILEVEL_DEFAULT = 1,    // default UI is used
		INSTALLUILEVEL_NONE = 2,    // completely silent installation
		INSTALLUILEVEL_BASIC = 3,    // simple progress and error handling
		INSTALLUILEVEL_REDUCED = 4,    // authored UI, wizard dialogs suppressed
		INSTALLUILEVEL_FULL = 5,    // authored UI with wizards, progress, errors
		INSTALLUILEVEL_ENDDIALOG = 0x80, // display success/failure dialog at end of install
		INSTALLUILEVEL_PROGRESSONLY = 0x40, // display only progress dialog
		INSTALLUILEVEL_HIDECANCEL = 0x20, // do not display the cancel button in basic UI
		INSTALLUILEVEL_SOURCERESONLY = 0x100, // force display of source resolution even if quiet
	}

	public enum INSTALLMESSAGE
	{
		INSTALLMESSAGE_FATALEXIT = 0x00000000, // premature termination, possibly fatal OOM
		INSTALLMESSAGE_ERROR = 0x01000000, // formatted error message
		INSTALLMESSAGE_WARNING = 0x02000000, // formatted warning message
		INSTALLMESSAGE_USER = 0x03000000, // user request message
		INSTALLMESSAGE_INFO = 0x04000000, // informative message for log
		INSTALLMESSAGE_FILESINUSE = 0x05000000, // list of files in use that need to be replaced
		INSTALLMESSAGE_RESOLVESOURCE = 0x06000000, // request to determine a valid source location
		INSTALLMESSAGE_OUTOFDISKSPACE = 0x07000000, // insufficient disk space message
		INSTALLMESSAGE_ACTIONSTART = 0x08000000, // start of action: action name & description
		INSTALLMESSAGE_ACTIONDATA = 0x09000000, // formatted data associated with individual action item
		INSTALLMESSAGE_PROGRESS = 0x0A000000, // progress gauge info: units so far, total
		INSTALLMESSAGE_COMMONDATA = 0x0B000000, // product info for dialog: language Id, dialog caption
		INSTALLMESSAGE_INITIALIZE = 0x0C000000, // sent prior to UI initialization, no string data
		INSTALLMESSAGE_TERMINATE = 0x0D000000, // sent after UI termination, no string data
		INSTALLMESSAGE_SHOWDIALOG = 0x0E000000 // sent prior to display or authored dialog or wizard
	}

	public enum INSTALLLOGMODE  // bit flags for use with MsiEnableLog and MsiSetExternalUI
	{
		INSTALLLOGMODE_FATALEXIT = (1 << (INSTALLMESSAGE.INSTALLMESSAGE_FATALEXIT >> 24)),
		INSTALLLOGMODE_ERROR = (1 << (INSTALLMESSAGE.INSTALLMESSAGE_ERROR >> 24)),
		INSTALLLOGMODE_WARNING = (1 << (INSTALLMESSAGE.INSTALLMESSAGE_WARNING >> 24)),
		INSTALLLOGMODE_USER = (1 << (INSTALLMESSAGE.INSTALLMESSAGE_USER >> 24)),
		INSTALLLOGMODE_INFO = (1 << (INSTALLMESSAGE.INSTALLMESSAGE_INFO >> 24)),
		INSTALLLOGMODE_RESOLVESOURCE = (1 << (INSTALLMESSAGE.INSTALLMESSAGE_RESOLVESOURCE >> 24)),
		INSTALLLOGMODE_OUTOFDISKSPACE = (1 << (INSTALLMESSAGE.INSTALLMESSAGE_OUTOFDISKSPACE >> 24)),
		INSTALLLOGMODE_ACTIONSTART = (1 << (INSTALLMESSAGE.INSTALLMESSAGE_ACTIONSTART >> 24)),
		INSTALLLOGMODE_ACTIONDATA = (1 << (INSTALLMESSAGE.INSTALLMESSAGE_ACTIONDATA >> 24)),
		INSTALLLOGMODE_COMMONDATA = (1 << (INSTALLMESSAGE.INSTALLMESSAGE_COMMONDATA >> 24)),
		INSTALLLOGMODE_PROPERTYDUMP = (1 << (INSTALLMESSAGE.INSTALLMESSAGE_PROGRESS >> 24)), // log only
		INSTALLLOGMODE_VERBOSE = (1 << (INSTALLMESSAGE.INSTALLMESSAGE_INITIALIZE >> 24)), // log only
		INSTALLLOGMODE_EXTRADEBUG = (1 << (INSTALLMESSAGE.INSTALLMESSAGE_TERMINATE >> 24)), // log only
		INSTALLLOGMODE_LOGONLYONERROR = (1 << (INSTALLMESSAGE.INSTALLMESSAGE_SHOWDIALOG >> 24)), // log only    
		INSTALLLOGMODE_PROGRESS = (1 << (INSTALLMESSAGE.INSTALLMESSAGE_PROGRESS >> 24)), // external handler only
		INSTALLLOGMODE_INITIALIZE = (1 << (INSTALLMESSAGE.INSTALLMESSAGE_INITIALIZE >> 24)), // external handler only
		INSTALLLOGMODE_TERMINATE = (1 << (INSTALLMESSAGE.INSTALLMESSAGE_TERMINATE >> 24)), // external handler only
		INSTALLLOGMODE_SHOWDIALOG = (1 << (INSTALLMESSAGE.INSTALLMESSAGE_SHOWDIALOG >> 24)), // external handler only
		INSTALLLOGMODE_FILESINUSE = (1 << (INSTALLMESSAGE.INSTALLMESSAGE_FILESINUSE >> 24)), // external handler only
	}

	public enum INSTALLLOGATTRIBUTES // flag attributes for MsiEnableLog
	{
		INSTALLLOGATTRIBUTES_APPEND = (1 << 0),
		INSTALLLOGATTRIBUTES_FLUSHEACHLINE = (1 << 1),
	}


    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<msiPath>")]
        public string MsiPath { get; set; }

        [CommandArgument(1, "<destFolder>")]
        public string DestinationFolder { get; set; }

        [CommandArgument(2, "[logFile]")]
        public string LogFile { get; set; }
    }


    public override int Execute(CommandContext context, Settings settings)
    {
		Directory.CreateDirectory(settings.DestinationFolder);

		var hwnd = IntPtr.Zero;
		MsiSetInternalUI(INSTALLUILEVEL.INSTALLUILEVEL_NONE, ref hwnd);
		if (!string.IsNullOrEmpty(settings.LogFile))
			MsiEnableLog(INSTALLLOGMODE.INSTALLLOGMODE_INFO, settings.LogFile, INSTALLLOGATTRIBUTES.INSTALLLOGATTRIBUTES_FLUSHEACHLINE);

		// 'ADMIN' action performs network drive install (extraction only)
		var result = PInvoke.Msi.MsiInstallProduct(settings.MsiPath, $"ACTION=ADMIN TARGETDIR=\"{settings.DestinationFolder}\"");
        return (int)result;
    }
}

internal class DownloadPbiDesktopExeCommand : AsyncCommand<DownloadPbiDesktopExeCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<fileName>")]
        public string FileName { get; set; }

        [CommandArgument(1, "<destFolder>")]
        public string DestinationFolder { get; set; }
    }

	private static readonly string DownloadUrlBase = "https://download.microsoft.com/download/8/8/0/880BCA75-79DD-466A-927D-1ABF1F5454B0/";

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var uri = settings.FileName.StartsWith("http", StringComparison.InvariantCultureIgnoreCase)
            ? new Uri(settings.FileName)
            : new Uri($"{DownloadUrlBase}{settings.FileName}");

        var fileName = Path.GetFileName(uri.AbsoluteUri);
        var destination = new FileInfo(Path.Combine(settings.DestinationFolder, fileName));
        destination.Directory.Create();

        using var http = new HttpClient();
		using var stream = await http.GetStreamAsync(uri);
		using var file = File.Create(destination.FullName);

        await stream.CopyToAsync(file);

        return 0;
    }
}