﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Caffeinated.Properties;

namespace Caffeinated {
    public class Duration {
        public int Minutes { get; set; }
        public string Description {
            get {
                return Duration.ToDescription(this.Minutes);
            }
        }

        public static string ToDescription(int time) {
            if (time == -1) {
                return "Auto (App list)";
            }
            if (time == 0) {
                return "Indefinitely";
            }
            int mins = time % 60;
            if (mins != 0) {
                return String.Format("{0} minutes", mins);
            }
            else {
                int hours = time / 60;
                if (hours == 1) {
                    return "1 hour";
                }
                else {
                    return String.Format("{0} hours", hours);
                }
            }
        }
    }

    internal static class NativeMethods {
        [DllImport("kernel32.dll")]
        public static extern uint SetThreadExecutionState(uint esFlags);
        public const uint ES_CONTINUOUS = 0x80000000;
        public const uint ES_SYSTEM_REQUIRED = 0x00000001;
        public const uint ES_DISPLAY_REQUIRED = 0x00000002;
        public const uint ES_AWAYMODE_REQUIRED = 0x00000040;
    }

    public class AppContext : ApplicationContext {
        private NotifyIcon notifyIcon;
        private IContainer components;
        private Icon onIcon;
        private Icon offIcon;
        private Icon autoOnIcon;
        private Icon autoOffIcon;
        private Timer timer;
        private Timer autoTimer;
        private SettingsForm settingsForm = null;
        private AboutForm aboutForm = null;

        [STAThread]
        static void Main() {
            var context = new AppContext();
            Application.Run(context);
        }

        public AppContext() {

            this.components = new Container();
            this.timer = new Timer(components);
            timer.Tick += new EventHandler(timer_Tick);

            this.autoTimer = new Timer(components);
            autoTimer.Tick += new EventHandler(autoTimer_Tick);
            autoTimer.Interval = 1 * 10 * 1000; // Check every 10 seconds.

            var contextMenu = new ContextMenu();

            var exitItem = new MenuItem("E&xit");
            exitItem.Click += new EventHandler(this.exitItem_Click);

            // we want the lower durations to be closer to the mouse. So, 
            var times = Settings.Default.RealDurations;
            IEnumerable<int> sortedTimes = Enumerable.Empty<int>();
            if ((new Taskbar()).Position == TaskbarPosition.Top) {
                sortedTimes = times.OrderBy(i => i);
            }
            else {
                sortedTimes = times.OrderByDescending(i => i);
            }

            var activateForItem = new MenuItem("&Stay awake for");
            foreach (var time in sortedTimes) {
                var item = new MenuItem(Duration.ToDescription(time));
                item.Tag = time; 
                item.Click += new EventHandler(item_Click);
                activateForItem.MenuItems.Add(item);
            }

            var settingsItem = new MenuItem("&Settings...");
            settingsItem.Click += new EventHandler(settingsItem_Click);

            var aboutItem = new MenuItem("&About...");
            aboutItem.Click += new EventHandler(aboutItem_Click);

            contextMenu.MenuItems.AddRange(
                new MenuItem[] { 
                    activateForItem, 
                    new MenuItem("-"),
                    settingsItem,
                    aboutItem,
                    exitItem
                }
            );

            this.offIcon = new Icon(
                Properties.Resources.sleeping_icon_white,
                SystemInformation.SmallIconSize
            );
            this.onIcon = new Icon(
                Properties.Resources.surprised_icon_white,
                SystemInformation.SmallIconSize
            );
            this.autoOffIcon = new Icon(
                Properties.Resources.sleeping_icon_blue,
                SystemInformation.SmallIconSize
            );
            this.autoOnIcon = new Icon(
                Properties.Resources.surprised_icon_blue,
                SystemInformation.SmallIconSize
            );
            this.notifyIcon = new NotifyIcon(this.components);

            notifyIcon.ContextMenu = contextMenu;

            // tooltip
            notifyIcon.Text = "Caffeinated";
            notifyIcon.Visible = true;

            // Handle the DoubleClick event to activate the form.
            notifyIcon.MouseClick += new MouseEventHandler(notifyIcon1_Click);

            if (Settings.Default.ActivateAtLaunch) {
                activate(Settings.Default.DefaultDuration);
            }
            else {
                deactivate(false);
            }
            if (Settings.Default.ShowSettingsAtLaunch) {
                showSettings();
            }
        }

        void aboutItem_Click(object sender, EventArgs e) {
            aboutForm = new AboutForm();
            aboutForm.Show();
        }

        void settingsItem_Click(object sender, EventArgs e) {
            showSettings();
        }

        void showSettings() {
            settingsForm = new SettingsForm();
            settingsForm.Show();
        }

        void timer_Tick(object sender, EventArgs e) {
            deactivate(false);
        }

        void autoTimer_Tick(object sender, EventArgs e) {
            if (isAutoAppRunning()) {
                activate(-1);
            }
            else {
                deactivate(true);
            }
        }

        bool isAutoAppRunning() {
            HashSet<String> appSet = new HashSet<String>(Settings.Default.AutoAppList.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries));
            Process[] processlist = Process.GetProcesses();
            foreach (Process theprocess in processlist) {
                if (appSet.Contains(theprocess.ProcessName)) return true;
            }
            return false;
        }

        void item_Click(object sender, EventArgs e) {
            int time = (int)((MenuItem)sender).Tag;
            if (time == -1) {
                autoTimer_Tick(null, null);
                autoTimer.Start();
                return;
            }
            autoTimer.Stop();
            this.activate(time);
        }

        void notifyIcon1_Click(object sender, MouseEventArgs e) {
            if (e.Button != MouseButtons.Left) return;
            if (this.isActive()) 
                this.deactivate(false);
            else
                this.activate(Settings.Default.DefaultDuration);
        }

        void ShowError() {
            MessageBox.Show(
                "Call to SetThreadExecutionState failed.",
                "Caffeinated",
                MessageBoxButtons.OK
            );
        }

        bool isActive() {
            return (notifyIcon.Icon == onIcon) || (notifyIcon.Icon == autoOnIcon) || (notifyIcon.Icon == autoOffIcon);
        }

        void activate(int duration) {
            uint sleepDisabled = NativeMethods.ES_CONTINUOUS |
                                NativeMethods.ES_SYSTEM_REQUIRED |
                                NativeMethods.ES_AWAYMODE_REQUIRED;

            if (Settings.Default.KeepMonitorOn) {
                sleepDisabled |= NativeMethods.ES_DISPLAY_REQUIRED;
            }

            uint previousState = NativeMethods.SetThreadExecutionState(sleepDisabled);
            if (previousState == 0) {
                ShowError();
                ExitThread();
            }
            if (duration > 0) {
                this.timer.Interval = duration * 60 * 1000;
                this.timer.Start();
            }
            if (duration == -1) {
                if (!autoTimer.Enabled)
                    autoTimer.Start();
                this.notifyIcon.Icon = autoOnIcon;
                this.notifyIcon.Text = "Caffeinated: (auto) sleep not allowed!";
            }
            else
            { 
                this.notifyIcon.Icon = onIcon;
                this.notifyIcon.Text = "Caffeinated: sleep not allowed!";
            }
        }

        private void deactivate(bool auto)
        {
            timer.Stop();
            uint result = NativeMethods.SetThreadExecutionState(NativeMethods.ES_CONTINUOUS);
            if (result == 0) {
                ShowError();
            }
            if (auto) {
                this.notifyIcon.Icon = autoOffIcon;
                this.notifyIcon.Text = "Caffeinated: (auto) sleep allowed";
            }
            else {
                autoTimer.Stop();
                this.notifyIcon.Icon = offIcon;
                this.notifyIcon.Text = "Caffeinated: sleep allowed";
            }
        }

        private void exitItem_Click(object Sender, EventArgs e) {
            deactivate(false);
            ExitThread();
        }

        protected override void Dispose(bool disposing) {
            if (disposing && components != null)
                components.Dispose();
            base.Dispose(disposing);
        }
    }
}