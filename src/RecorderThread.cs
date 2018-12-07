using System;
using System.IO;
using System.Net;
using System.Threading;
using NAudio.Wave;

namespace VOX2FTP
{
    class RecorderThread : IDisposable
    {
        /// <summary>
        /// Sleep delay between audio data processing
        /// </summary>
        private const int ThreadDelay = 1000;

        /// <summary>
        /// Thread for audio data processing
        /// </summary>
        private Thread _thread;

        /// <summary>
        /// Is the thread running?
        /// </summary>
        public bool IsRunning => _thread.IsAlive;

        /// <summary>
        /// Path for local recordings
        /// </summary>
        private string RecordingPath;

        /// <summary>
        /// NAudio wav in device
        /// </summary>
        private WaveInEvent _waveIn;

        /// <summary>
        /// Local memorystream buffer
        /// </summary>
        private MemoryStream _stream = new MemoryStream();

        /// <summary>
        /// Index of the last detected activity
        /// </summary>
        private long LastActivityIndex = 0;

        /// <summary>
        /// Index of the last scan
        /// </summary>
        private long LastScanIndex = 0;

        /// <summary>
        /// Recording state
        /// </summary>
        enum RecordingState
        {
            IDLE,
            RECORDING
        };

        /// <summary>
        /// Current state of the recorder
        /// </summary>
        private RecordingState _state = RecordingState.IDLE;

        public RecorderThread()
        {
            _thread = new Thread(Thread)
            {
                Priority = ThreadPriority.BelowNormal,
                Name = "VOX2FTP-Recorder"
            };

            RecordingPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VOX2FTP", "Recordings");
            if (!Directory.Exists(RecordingPath))
            {
                Directory.CreateDirectory(RecordingPath);
            }
        }

        /// <summary>
        /// Start the recorder
        /// </summary>
        public void Start()
        {
            // TODO - yeah, this obviously doesn't pick back up after we stop it, need to fix that
            if (_thread.IsAlive) return;
            if (_thread.ThreadState == ThreadState.Aborted)
                _thread = new Thread(Thread);
            _thread.Start();
        }

        /// <summary>
        /// Stop the recorder
        /// </summary>
        public void Stop()
        {
            if (_thread.IsAlive)
                _thread.Abort();
        }

        /// <summary>
        /// Calculate if current wav byte is silent audio
        /// </summary>
        /// <param name="amplitude">input audio</param>
        /// <returns>bool</returns>
        private static bool IsSilence(float amplitude)
        { 
            var dB = Math.Abs(20 * Math.Log10(Math.Abs(amplitude / 256)));
            return dB < (sbyte) App.Settings.VoxThreshold;
        }

        /// <summary>
        /// Upload the current memory buffer to the FTP site
        /// </summary>
        private void UploadBuffer()
        {
            var FileIdentifier = Guid.NewGuid();
            var outFile = Path.Combine(RecordingPath, $"{FileIdentifier}.wav");
            using (var wav = new WaveFileWriter(outFile, _waveIn.WaveFormat))
            {
                wav.Write(_stream.GetBuffer(), 0, (int) _stream.Length);
                wav.Flush();
            }
            using (var ftp = new WebClient())
            {
                ftp.Credentials = new NetworkCredential(App.Settings.FtpUser, App.Settings.DecryptedPass);
                ftp.UploadFile(
                    $"ftp://{App.Settings.FtpHost}:{App.Settings.FtpPort}/{App.Settings.FtpPath}/{Path.GetFileName(outFile)}",
                    WebRequestMethods.Ftp.UploadFile, outFile);
            }
            // TODO - this should get reported to the front-end
        }

        /// <summary>
        /// Main audio processing thread
        /// </summary>
        private void Thread()
        {
            try
            {
                _waveIn = new WaveInEvent
                {
                    DeviceNumber = App.Settings.MicInputIndex,
                    WaveFormat = new WaveFormat(16000, 8, 1)
                };
                _waveIn.DataAvailable += WaveSourceDataAvailable;
                _waveIn.StartRecording();
                while (true)
                {
                    switch (_state)
                    {
                        case RecordingState.IDLE:
                        {
                            // check for activity
                            var nonSilentBytes = 0;
                            _stream.Position = LastScanIndex;
                            for (var i = LastScanIndex; i < _stream.Length; i++)
                            {
                                if (!IsSilence(_stream.ReadByte()))
                                {
                                    ++nonSilentBytes;
                                }
                            }

                            if (nonSilentBytes > (_stream.Length - LastScanIndex) / 100)
                            {
                                _state = RecordingState.RECORDING;
                                _stream.Position = _stream.Length - 1;
                            }
                            else
                            {
                                _stream.SetLength(0);
                            }

                            break;
                        }
                        case RecordingState.RECORDING:
                        {
                            // look for inactivity and upload if needed
                            var silentBytes = 0;
                            _stream.Position = LastScanIndex;
                            for (var i = LastScanIndex; i < _stream.Length; ++i)
                            {
                                if (IsSilence(_stream.ReadByte()))
                                {
                                    ++silentBytes;
                                }
                            }

                            var sampleSize = _stream.Length - LastScanIndex;


                            if (silentBytes > (_stream.Length - LastScanIndex) / 100)
                            {
                                _state = RecordingState.IDLE;
                                _stream.Position = 0;
                                UploadBuffer();
                                _stream.SetLength(0);
                            }
                            else
                            {
                                _stream.Position = _stream.Length - 1;
                            }

                            break;
                        }
                    }
                    LastScanIndex = _stream.Length;
                    // sleep
                    System.Threading.Thread.Sleep(ThreadDelay);
                }
            }
            catch (ThreadAbortException e)
            {
                _waveIn.StopRecording();
                _waveIn.Dispose();
            }
        }

        /// <summary>
        /// Event handler when new data is available from our recording device
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void WaveSourceDataAvailable(object sender, WaveInEventArgs e)
        {
            // write our findings to our memory stream
            if (_stream == null)
            {
                _stream = new MemoryStream();
            }
            _stream.Write(e.Buffer, 0, e.BytesRecorded);
            _stream.Flush();
        }

        public void Dispose()
        {
            if (_thread.IsAlive)
                _thread.Abort();            
        }
    }
}
