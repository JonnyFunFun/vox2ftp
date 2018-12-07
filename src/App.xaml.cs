using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows;

namespace VOX2FTP
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static RecorderSettings Settings;

        public App()
        {
            var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                "VOX2FTP");
            if (!Directory.Exists(settingsPath))
            {
                Directory.CreateDirectory(settingsPath);
            }

            Settings = File.Exists(Path.Combine(settingsPath, "Settings.xml")) ? 
                RecorderSettings.Load(Path.Combine(settingsPath, "Settings.xml")) : 
                new RecorderSettings();
        }
    }
}
