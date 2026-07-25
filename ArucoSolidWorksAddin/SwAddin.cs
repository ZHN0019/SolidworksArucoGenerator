using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;

namespace ArucoSolidWorksAddin
{
    [ComVisible(true)]
    [Guid(AddinGuid)]
    [ProgId("Codex.ArucoSolidWorksAddin")]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public sealed class SwAddin : ISwAddin
    {
        public const string AddinGuid = "78E6B279-EA99-4BD3-8C1B-CB1C8A309DF1";
        private const int CommandGroupId = 41001;
        private SldWorks _application;
        private CommandManager _commandManager;
        private CommandGroup _commandGroup;
        private GeneratorForm _form;
        public string LastConnectError { get; private set; }

        public bool ConnectToSW(object thisSw, int cookie)
        {
            try
            {
                LastConnectError = null;
                _application = (SldWorks)thisSw;
                if (!_application.SetAddinCallbackInfo2(0, this, cookie))
                    throw new InvalidOperationException("SetAddinCallbackInfo2 returned false.");
                _commandManager = _application.GetCommandManager(cookie);
                AddCommandManager();
                WriteConnectLog("ConnectToSW succeeded.");
                return true;
            }
            catch (Exception ex)
            {
                LastConnectError = ex.ToString();
                WriteConnectLog(LastConnectError);
                return false;
            }
        }

        public bool DisconnectFromSW()
        {
            try
            {
                if (_form != null && !_form.IsDisposed)
                    _form.Close();
                _form = null;

                if (_commandManager != null)
                    _commandManager.RemoveCommandGroup2(CommandGroupId, true);
                _commandGroup = null;
                _commandManager = null;

                if (_application != null && Marshal.IsComObject(_application))
                    Marshal.ReleaseComObject(_application);
                _application = null;
                GC.Collect();
                GC.WaitForPendingFinalizers();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void ShowGenerator()
        {
            if (_application == null)
                return;

            if (_form == null || _form.IsDisposed)
            {
                _form = new GeneratorForm(_application);
                _form.FormClosed += (_, __) => _form = null;
                _form.Show();
            }
            else
            {
                if (_form.WindowState == System.Windows.Forms.FormWindowState.Minimized)
                    _form.WindowState = System.Windows.Forms.FormWindowState.Normal;
                _form.Show();
                _form.Activate();
            }
        }

        public int CanShowGenerator()
        {
            return _application == null ? 0 : 1;
        }

        private void AddCommandManager()
        {
            int errors = 0;
            _commandGroup = _commandManager.CreateCommandGroup2(
                CommandGroupId,
                "ArUco 生成器",
                "创建双实体 ArUco 标记零件",
                "创建 ArUco 标记",
                -1,
                true,
                ref errors);
            if (_commandGroup == null)
                throw new InvalidOperationException("Could not create the ArUco command group.");

            int menuOnly = (int)swCommandItemType_e.swMenuItem;
            int commandIndex = _commandGroup.AddCommandItem2(
                "生成 ArUco",
                -1,
                "打开 ArUco 参数生成界面",
                "生成 ArUco",
                -1,
                nameof(ShowGenerator),
                nameof(CanShowGenerator),
                1,
                menuOnly);
            if (commandIndex < 0)
                throw new InvalidOperationException("Could not add the ArUco command item.");

            _commandGroup.HasMenu = true;
            _commandGroup.HasToolbar = false;
            if (!_commandGroup.Activate())
                throw new InvalidOperationException("Could not activate the ArUco command group.");
        }

        private static void WriteConnectLog(string text)
        {
            try
            {
                string directory = Path.Combine(
                    System.Environment.GetFolderPath(
                        System.Environment.SpecialFolder.LocalApplicationData),
                    "ArucoSolidWorksAddin");
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "connect.log"),
                    DateTime.Now.ToString("O") + System.Environment.NewLine + text);
            }
            catch
            {
                // Connection must not crash SOLIDWORKS because diagnostics failed.
            }
        }

        [ComRegisterFunction]
        public static void Register(Type type)
        {
            string guid = "{" + AddinGuid + "}";
            using (RegistryKey localMachine = RegistryKey.OpenBaseKey(
                RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey key = localMachine.CreateSubKey(
                @"SOFTWARE\SOLIDWORKS\Addins\" + guid))
            {
                key.SetValue(null, 1, RegistryValueKind.DWord);
                key.SetValue("Title", "ArUco 零件生成器", RegistryValueKind.String);
                key.SetValue("Description",
                    "创建 DICT_4X4_50 双实体 ArUco 零件及同名 PNG、STEP",
                    RegistryValueKind.String);
            }

            using (RegistryKey currentUser = RegistryKey.OpenBaseKey(
                RegistryHive.CurrentUser, RegistryView.Registry64))
            using (RegistryKey key = currentUser.CreateSubKey(
                @"SOFTWARE\SOLIDWORKS\AddInsStartup"))
            {
                key.SetValue(guid, 1, RegistryValueKind.DWord);
            }
        }

        [ComUnregisterFunction]
        public static void Unregister(Type type)
        {
            string guid = "{" + AddinGuid + "}";
            using (RegistryKey localMachine = RegistryKey.OpenBaseKey(
                RegistryHive.LocalMachine, RegistryView.Registry64))
            {
                localMachine.DeleteSubKeyTree(
                    @"SOFTWARE\SOLIDWORKS\Addins\" + guid, false);
            }

            using (RegistryKey currentUser = RegistryKey.OpenBaseKey(
                RegistryHive.CurrentUser, RegistryView.Registry64))
            using (RegistryKey key = currentUser.OpenSubKey(
                @"SOFTWARE\SOLIDWORKS\AddInsStartup", true))
            {
                key?.DeleteValue(guid, false);
            }
        }
    }
}
