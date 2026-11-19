using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;
using mRemoteNG.App;
using System.Threading;
using System.Globalization;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.App.Info;
using mRemoteNG.Messages;
using mRemoteNG.Tools;
using mRemoteNG.UI.Controls;
using mRemoteNG.UI.Forms;
using System.Runtime.Versioning;

namespace mRemoteNG.Config.Settings
{
    [SupportedOSPlatform("windows")]
    public class SettingsLoader
    {
        private readonly ExternalAppsLoader _externalAppsLoader;
        private readonly MessageCollector _messageCollector;
        private readonly MenuStrip _mainMenu;
        private readonly QuickConnectToolStrip _quickConnectToolStrip;
        private readonly ExternalToolsToolStrip _externalToolsToolStrip;
        private readonly MultiSshToolStrip _multiSshToolStrip;

        private FrmMain MainForm { get; }


        public SettingsLoader(FrmMain mainForm, MessageCollector messageCollector, QuickConnectToolStrip quickConnectToolStrip, ExternalToolsToolStrip externalToolsToolStrip, MultiSshToolStrip multiSshToolStrip, MenuStrip mainMenu)
        {
            MainForm = mainForm ?? throw new ArgumentNullException(nameof(mainForm));
            _messageCollector = messageCollector ?? throw new ArgumentNullException(nameof(messageCollector));
            _quickConnectToolStrip = quickConnectToolStrip ?? throw new ArgumentNullException(nameof(quickConnectToolStrip));
            _externalToolsToolStrip = externalToolsToolStrip ?? throw new ArgumentNullException(nameof(externalToolsToolStrip));
            _multiSshToolStrip = multiSshToolStrip ?? throw new ArgumentNullException(nameof(multiSshToolStrip));
            _mainMenu = mainMenu ?? throw new ArgumentNullException(nameof(mainMenu));
            _externalAppsLoader = new ExternalAppsLoader(MainForm, messageCollector, _externalToolsToolStrip);
        }

        #region Public Methods

        public void LoadSettings()
        {
            try
            {
                SetSupportedCulture();
                SetApplicationWindowPositionAndSize();
                SetKioskMode();

                SetPuttyPath();
                SetShowSystemTrayIcon();
                SetAutoSave();
                LoadExternalAppsFromXml();

                if (Properties.App.Default.ResetToolbars)
                    SetToolbarsDefault();
                else
                    LoadToolbarsFromSettings();
            }
            catch (Exception ex)
            {
                _messageCollector.AddExceptionMessage("Loading settings failed", ex);
            }
        }


        private void SetSupportedCulture()
        {
            if (Properties.Settings.Default.OverrideUICulture == "" || !SupportedCultures.IsNameSupported(Properties.Settings.Default.OverrideUICulture)) return;
            ProgramRoot.ApplyUiCulture(Properties.Settings.Default.OverrideUICulture);
            _messageCollector.AddMessage(MessageClass.InformationMsg, $"Override Culture: {Thread.CurrentThread.CurrentUICulture.Name}/{Thread.CurrentThread.CurrentUICulture.NativeName}", true);
        }

        private void SetApplicationWindowPositionAndSize()
        {
            MainForm.WindowState = FormWindowState.Normal;
            if (Properties.App.Default.MainFormState == FormWindowState.Normal)
            {
                if (!Properties.App.Default.MainFormLocation.IsEmpty)
                    MainForm.Location = Properties.App.Default.MainFormLocation;
                if (!Properties.App.Default.MainFormSize.IsEmpty)
                    MainForm.Size = Properties.App.Default.MainFormSize;
            }
            else
            {
                if (!Properties.App.Default.MainFormRestoreLocation.IsEmpty)
                    MainForm.Location = Properties.App.Default.MainFormRestoreLocation;
                if (!Properties.App.Default.MainFormRestoreSize.IsEmpty)
                    MainForm.Size = Properties.App.Default.MainFormRestoreSize;
            }

            if (Properties.App.Default.MainFormState == FormWindowState.Maximized)
            {
                MainForm.WindowState = FormWindowState.Maximized;
            }

            // Make sure the form is visible on the screen
            const int minHorizontal = 300;
            const int minVertical = 150;
            Rectangle screenBounds = Screen.FromHandle(MainForm.Handle).Bounds;
            Rectangle newBounds = MainForm.Bounds;

            if (newBounds.Right < screenBounds.Left + minHorizontal)
                newBounds.X = screenBounds.Left + minHorizontal - newBounds.Width;
            if (newBounds.Left > screenBounds.Right - minHorizontal)
                newBounds.X = screenBounds.Right - minHorizontal;
            if (newBounds.Bottom < screenBounds.Top + minVertical)
                newBounds.Y = screenBounds.Top + minVertical - newBounds.Height;
            if (newBounds.Top > screenBounds.Bottom - minVertical)
                newBounds.Y = screenBounds.Bottom - minVertical;

            MainForm.Location = newBounds.Location;
        }

        private void SetAutoSave()
        {
            if (Properties.OptionsConnectionsPage.Default.AutoSaveEveryMinutes <= 0) return;
            MainForm.tmrAutoSave.Interval = Properties.OptionsConnectionsPage.Default.AutoSaveEveryMinutes * 60000;
            MainForm.tmrAutoSave.Enabled = true;
        }

        private void SetKioskMode()
        {
            if (!Properties.App.Default.MainFormKiosk) return;
            MainForm.Fullscreen.Value = true;
        }

        private static void SetShowSystemTrayIcon()
        {
            if (Properties.OptionsAppearancePage.Default.ShowSystemTrayIcon)
                Runtime.NotificationAreaIcon = new NotificationAreaIcon();
        }

        private static void SetPuttyPath()
        {
            PuttyBase.PuttyPath = Properties.OptionsAdvancedPage.Default.UseCustomPuttyPath ? Properties.OptionsAdvancedPage.Default.CustomPuttyPath : GeneralAppInfo.PuttyPath;
        }

        private void SetToolbarsDefault()
        {
            ResetAllToolbarLocations();
            AddMainMenuPanel();

            _quickConnectToolStrip.Visible = true;
            JoinOnMainMenuRow(_quickConnectToolStrip, 0);

            ToolStripPanelFromString("bottom").Join(_externalToolsToolStrip, new Point(3, 0));
            _externalToolsToolStrip.Visible = false;
        }

        private void LoadToolbarsFromSettings()
        {
            ResetAllToolbarLocations();
            AddMainMenuPanel();

            // Toolbars saved on the first row of the top panel must be joined relative to the
            // main menu (and to each other) instead of at their absolute saved X. The menu
            // width depends on the UI language, theme and DPI, so an X that used to fit to the
            // right of the menu can overlap it after a restart; ToolStripPanel.Join then
            // silently moves the toolbar to a new row and can push the menu to the right.
            List<(ToolStrip Toolbar, Point SavedLocation)> mainMenuRowToolbars = [];

            AddExternalAppsPanel(mainMenuRowToolbars);
            AddQuickConnectPanel(mainMenuRowToolbars);
            AddMultiSshPanel(mainMenuRowToolbars);

            JoinMainMenuRowToolbars(mainMenuRowToolbars);
        }

        /// <summary>
        /// This prevents odd positioning issues due to toolbar load order.
        /// Since all toolbars start in this temp panel, no toolbar load
        /// can be blocked by pre-existing toolbars.
        /// </summary>
        private void ResetAllToolbarLocations()
        {
            ToolStripPanel tempToolStrip = new();
            tempToolStrip.Join(_mainMenu);
            tempToolStrip.Join(_quickConnectToolStrip);
            tempToolStrip.Join(_externalToolsToolStrip);
            tempToolStrip.Join(_multiSshToolStrip);
        }

        private void AddMainMenuPanel()
        {
            SetToolstripGripStyle(_mainMenu);
            ToolStripPanel toolStripPanel = ToolStripPanelFromString("top");
            toolStripPanel.Join(_mainMenu, new Point(3, 0));

            // The menu must be laid out before other toolbars are joined next to it,
            // otherwise its Width is still stale and the following toolbars overlap it.
            toolStripPanel.PerformLayout();
            _mainMenu.PerformLayout();
        }

        private void AddQuickConnectPanel(List<(ToolStrip Toolbar, Point SavedLocation)> mainMenuRowToolbars)
        {
            SetToolstripGripStyle(_quickConnectToolStrip);
            _quickConnectToolStrip.Visible = Properties.Settings.Default.QuickyTBVisible;
            AddToolbarPanel(_quickConnectToolStrip, Properties.Settings.Default.QuickyTBParentDock,
                            Properties.Settings.Default.QuickyTBLocation, mainMenuRowToolbars);
        }

        private void AddExternalAppsPanel(List<(ToolStrip Toolbar, Point SavedLocation)> mainMenuRowToolbars)
        {
            SetToolstripGripStyle(_externalToolsToolStrip);
            _externalToolsToolStrip.Visible = Properties.Settings.Default.ExtAppsTBVisible;
            AddToolbarPanel(_externalToolsToolStrip, Properties.Settings.Default.ExtAppsTBParentDock,
                            Properties.Settings.Default.ExtAppsTBLocation, mainMenuRowToolbars);
        }

        private void AddMultiSshPanel(List<(ToolStrip Toolbar, Point SavedLocation)> mainMenuRowToolbars)
        {
            SetToolstripGripStyle(_multiSshToolStrip);
            _multiSshToolStrip.Visible = Properties.Settings.Default.MultiSshToolbarVisible;
            AddToolbarPanel(_multiSshToolStrip, Properties.Settings.Default.MultiSshToolbarParentDock,
                            Properties.Settings.Default.MultiSshToolbarLocation, mainMenuRowToolbars);
        }

        private void AddToolbarPanel(ToolStrip toolbar, string parentDock, Point savedLocation,
                                     List<(ToolStrip Toolbar, Point SavedLocation)> mainMenuRowToolbars)
        {
            // Defer the toolbars that belong to the main menu row so they can be joined
            // relative to the menu once it has been laid out.
            if (BelongsToMainMenuRow(parentDock, savedLocation))
            {
                mainMenuRowToolbars.Add((toolbar, savedLocation));
                return;
            }

            ToolStripPanelFromString(parentDock).Join(toolbar, savedLocation);
        }

        private void JoinMainMenuRowToolbars(List<(ToolStrip Toolbar, Point SavedLocation)> mainMenuRowToolbars)
        {
            // Preserve the user's left-to-right order on the row, chaining each toolbar
            // after the previously placed one so they never overlap.
            int x = _mainMenu.Right;
            foreach ((ToolStrip toolbar, _) in mainMenuRowToolbars.OrderBy(item => item.SavedLocation.X))
            {
                if (!toolbar.Visible)
                {
                    // Invisible toolbars still need a parent, but must not consume row space.
                    ToolStripPanelFromString("top").Join(toolbar, new Point(x, _mainMenu.Top));
                    continue;
                }

                x = JoinAt(toolbar, x);
            }
        }

        private void JoinOnMainMenuRow(ToolStrip toolbar, int extraSpacing)
        {
            JoinAt(toolbar, _mainMenu.Right + extraSpacing);
        }

        private int JoinAt(ToolStrip toolbar, int x)
        {
            ToolStripPanel toolStripPanel = ToolStripPanelFromString("top");
            toolStripPanel.Join(toolbar, new Point(x, _mainMenu.Top));
            toolStripPanel.PerformLayout();
            toolbar.PerformLayout();
            return toolbar.Right;
        }

        private static bool BelongsToMainMenuRow(string parentDock, Point savedLocation)
        {
            if (!string.Equals(parentDock, "Top", StringComparison.OrdinalIgnoreCase))
                return false;

            // Row 0 of the top panel holds the main menu, so its saved Y is always 0 regardless
            // of DPI or theme. Anything on a lower row keeps its absolute coordinates.
            return savedLocation.Y == 0;
        }

        private void SetToolstripGripStyle(ToolStrip toolbar)
        {
            toolbar.GripStyle = Properties.Settings.Default.LockToolbars ? ToolStripGripStyle.Hidden : ToolStripGripStyle.Visible;
        }

        private ToolStripPanel ToolStripPanelFromString(string panel)
        {
            switch (panel.ToLower())
            {
                case "top":
                    return MainForm.tsContainer.TopToolStripPanel;
                case "bottom":
                    return MainForm.tsContainer.BottomToolStripPanel;
                case "left":
                    return MainForm.tsContainer.LeftToolStripPanel;
                case "right":
                    return MainForm.tsContainer.RightToolStripPanel;
                default:
                    return MainForm.tsContainer.TopToolStripPanel;
            }
        }

        private void LoadExternalAppsFromXml()
        {
            _externalAppsLoader.LoadExternalAppsFromXML();
        }

        #endregion
    }
}