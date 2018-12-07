using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Image = System.Drawing.Image;

namespace VOX2FTP
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        extern static bool DestroyIcon(IntPtr handle);

        /// <summary>
        /// Main recorder thread
        /// </summary>
        private RecorderThread _thread;

        /// <summary>
        /// Are our local settings changed (dirty)
        /// </summary>
        private bool SettingsIsDirty = false;

        public MainWindow()
        {
            InitializeComponent();
            _thread = new RecorderThread();
            UpdateTaskBarIcon();
            UpdateButtonsEnabled();
            // load our settings
            FtpHostText.Text = App.Settings.FtpHost;
            FtpPathText.Text = App.Settings.FtpPath;
            FtpPortText.Text = App.Settings.FtpPort.ToString();
            FtpUserText.Text = App.Settings.FtpUser;
            ToleranceSlider.Value = App.Settings.VoxThreshold;
            AudioFormatBox.SelectedIndex = App.Settings.AudioFormat;
            MicInputBox.Items.Clear();
            foreach (var device in AudioUtils.GetAudioDevices())
            {
                var i = MicInputBox.Items.Add($"{device.Key}: {device.Value}");
                if (device.Key == App.Settings.MicInputIndex)
                {
                    MicInputBox.SelectedIndex = i;
                }
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        #region Taskbar Icon
        /// <summary>
        /// Updates the taskbar icon according to recording status
        /// </summary>
        private void UpdateTaskBarIcon()
        {
            var imgStream = Application
                .GetResourceStream(new Uri("pack://application:,,,/VOX2FTP;component/Assets/microphone.ico")).Stream;

            var img = Image.FromStream(imgStream);

            var icon = new Bitmap(img.Width, img.Height);
            var g = Graphics.FromImage(icon);

            g.DrawImage(img, new PointF(0, 0));
            g.Flush();
            g.Dispose();

            if (!_thread.IsRunning)
            {
                icon = MakeGrayscale3(icon);
            }

            var icoPtr = icon.GetHicon();
            var ico = System.Drawing.Icon.FromHandle(icoPtr);
            TaskbarIcon.Icon = ico;
        }

        /// <summary>
        /// Convert a Bitmap to a grayscale equivalent
        /// </summary>
        /// <param name="original">original bitmap</param>
        /// <returns>grayscale bitmap</returns>
        public static Bitmap MakeGrayscale3(Bitmap original)
        {
            //create a blank bitmap the same size as original
            var newBitmap = new Bitmap(original.Width, original.Height);

            //get a graphics object from the new image
            var g = Graphics.FromImage(newBitmap);

            //create the grayscale ColorMatrix
            var colorMatrix = new ColorMatrix(
                new float[][]
                {
                    new float[] {.3f, .3f, .3f, 0, 0},
                    new float[] {.59f, .59f, .59f, 0, 0},
                    new float[] {.11f, .11f, .11f, 0, 0},
                    new float[] {0, 0, 0, 1, 0},
                    new float[] {0, 0, 0, 0, 1}
                });

            //create some image attributes
            var attributes = new ImageAttributes();

            //set the color matrix attribute
            attributes.SetColorMatrix(colorMatrix);

            //draw the original image on the new image
            //using the gr
            g.DrawImage(original, new Rectangle(0, 0, original.Width, original.Height),
                0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);

            //dispose the Graphics object
            g.Dispose();
            return newBitmap;
        }
        #endregion

        private void TaskbarIcon_OnTrayMouseDoubleClick(object sender, RoutedEventArgs e)
        {
            Show();
        }

        /// <summary>
        /// Update which buttons are enabled or disabled
        /// </summary>
        private void UpdateButtonsEnabled()
        {
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = false;
            if (_thread.IsRunning)
            {
                StopButton.IsEnabled = true;
            }
            else
            {
                StartButton.IsEnabled = true;
            }
        }

        private void UpdateStatusLight()
        {
            StatusLed.IsChecked = _thread.IsRunning;
        }

        /// <summary>
        /// Start recording
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            _thread.Start();
            UpdateButtonsEnabled();
            UpdateTaskBarIcon();
            UpdateStatusLight();
        }

        /// <summary>
        /// Stop recording
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            _thread.Stop();
            UpdateButtonsEnabled();
            UpdateTaskBarIcon();
            UpdateStatusLight();
        }
        
        private void QuitButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VOX2FTP");
            if (!Directory.Exists(settingsPath))
            {
                Directory.CreateDirectory(settingsPath);
            }

            App.Settings.Save(Path.Combine(settingsPath, "Settings.xml"));
            Application.Current.Shutdown();
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("http://github.com/JonnyFunFun/vox2ftp");
        }

        /// <summary>
        /// Change handler to mark settings dirty
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SettingsChangeEvent(object sender, EventArgs e)
        {
            if (_thread == null) return;
            SettingsIsDirty = true;
            SaveSettingsButton.IsEnabled = true;
        }

        /// <summary>
        /// Save the settings XML
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // update our settings object
            var settings = App.Settings;
            var sel = MicInputBox.SelectedItem;
            if (sel == null)
            {
                MessageBox.Show("Mic input required!", "A selected mic input is required", MessageBoxButton.OK,
                    MessageBoxImage.Asterisk);
                return;
            }

            var itemStr = (string) MicInputBox.SelectedItem;
            var micIndex = itemStr.Split(':').First().Trim();

            SettingsIsDirty = false;
            SaveSettingsButton.IsEnabled = false;

            settings.MicInputIndex = int.Parse(micIndex);
            settings.VoxThreshold = ToleranceSlider.Value;
            settings.FtpHost = FtpHostText.Text;
            settings.FtpUser = FtpUserText.Text;
            if (FtpPassText.Password.Length > 0)
            {
                settings.FtpPass = FtpPassText.Password;
                FtpPassText.Password = "";
            }
            settings.FtpPath = FtpPathText.Text;
            settings.FtpPort = int.Parse(FtpPortText.Text);
            settings.AudioFormat = AudioFormatBox.SelectedIndex;

            var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VOX2FTP");
            if (!Directory.Exists(settingsPath))
            {
                Directory.CreateDirectory(settingsPath);
            }

            settings.Save(Path.Combine(settingsPath, "Settings.xml"));

            UpdateButtonsEnabled();
            UpdateTaskBarIcon();
            UpdateStatusLight();
        }

        /// <summary>
        /// Extra on-change handler to parse integer values
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FtpPortText_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            var i = FtpPortText.Text;
            int result;
            if (int.TryParse(i, out result))
            {
                FtpPortText.ClearValue(BorderBrushProperty);
                SettingsChangeEvent(sender, e);                
            }
            else
            {
                // invalid!
                FtpPortText.BorderBrush = System.Windows.Media.Brushes.Red;
                SaveSettingsButton.IsEnabled = false;
            }
        }
    }
}
