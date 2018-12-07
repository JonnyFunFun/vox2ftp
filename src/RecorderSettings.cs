using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Management;
using System.Text;
using System.Xml.Serialization;

namespace VOX2FTP
{
    [Serializable]
    [XmlRoot("RecorderSettings", Namespace = "http://jonnyfunfun.ninja/vox2ftp", IsNullable = false)]
    public class RecorderSettings : ISerializable
    {
        /// <summary>
        /// Index of the mic input device
        /// </summary>
        public int MicInputIndex { get; set; }

        /// <summary>
        /// Threshold (dB) for considering audio silence
        /// </summary>
        public double VoxThreshold { get; set; }

        /// <summary>
        /// Format to save recordings
        /// </summary>
        public int AudioFormat { get; set; }

        /// <summary>
        /// FTP hostname
        /// </summary>
        public string FtpHost { get; set; }

        /// <summary>
        /// FTP server port
        /// </summary>
        public int FtpPort { get; set; }

        /// <summary>
        /// FTP username
        /// </summary>
        public string FtpUser { get; set; }

        /// <summary>
        /// FTP password
        /// </summary>
        public string FtpPass { get; set; }

        /// <summary>
        /// FTP remote file path
        /// </summary>
        public string FtpPath { get; set; }

        private const string EncSalt = @"@eACj89GHBEPAmq6m*^!TG2m3w7!*Kkx";
        
        /// <summary>
        /// Build an encryption passphrase based on motherboard serial
        /// </summary>
        private string EncPassPhrase
        {
            get
            {
                var query = new ManagementObjectSearcher("root\\CIMV2", "SELECT * FROM Win32_baseboard");
                var bytes = Encoding.UTF8.GetBytes(
                    query.Get().Cast<ManagementBaseObject>()
                        .Aggregate("-", (current, queryObj) => current + queryObj["SerialNumber"]));
                return new SHA256Managed().ComputeHash(bytes)
                    .Aggregate(string.Empty, (current, x) => current + $"{x:x2}");
            }
        }

        public RecorderSettings()
        {
            // defaults
            MicInputIndex = 0;
            AudioFormat = 0;
            VoxThreshold = -40.0d;
            FtpHost = "localhost";
            FtpPort = 21;
            FtpUser = "anonymous";
            FtpPass = "";
            FtpPath = "/";
        }

        #region Serialization
        public RecorderSettings(SerializationInfo info, StreamingContext context)
        {
            MicInputIndex = (int) info.GetValue("input", typeof(int));
            VoxThreshold = (double) info.GetValue("threshold", typeof(double));
            FtpHost = (string) info.GetValue("host", typeof(string));
            FtpPort = (int) info.GetValue("port", typeof(int));
            FtpUser = (string) info.GetValue("user", typeof(string));
            var encPass = (string) info.GetValue("pass", typeof(string));
            FtpPass = Decrypt(encPass);
            FtpPath = (string) info.GetValue("path", typeof(string));
            AudioFormat = (int) info.GetValue("format", typeof(int));
        }

        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue("input", MicInputIndex, typeof(int));
            info.AddValue("threshold", VoxThreshold, typeof(double));
            info.AddValue("host", FtpHost, typeof(string));
            info.AddValue("port", FtpPort, typeof(int));
            info.AddValue("user", FtpUser, typeof(string));
            var encPass = Encrypt(FtpPass);
            info.AddValue("pass", encPass, typeof(string));
            info.AddValue("path", FtpPath, typeof(string));
            info.AddValue("format", AudioFormat, typeof(int));
        }

        public void Save(string filename)
        {
            var ser = new XmlSerializer(typeof(RecorderSettings));
            using (var sw = new StreamWriter(filename))
            {
                ser.Serialize(sw, this);
                sw.Flush();
            }
        }

        public static RecorderSettings Load(string filename)
        {
            var ser = new XmlSerializer(typeof(RecorderSettings));
            RecorderSettings ret;
            using (var fs = new FileStream(filename, FileMode.Open))
            {
                ret = (RecorderSettings)ser.Deserialize(fs);
            }
            return ret;
        }
        #endregion

        #region Password Encryption
        private string Encrypt(string data)
        {
            var key = new Rfc2898DeriveBytes(EncPassPhrase, Encoding.UTF8.GetBytes(EncSalt));
            using (var rij = new RijndaelManaged())
            {
                rij.Key = key.GetBytes(rij.KeySize / 8);
                using (var enc = rij.CreateEncryptor(rij.Key, rij.IV))
                {
                    using (var ms = new MemoryStream())
                    {
                        ms.Write(BitConverter.GetBytes(rij.IV.Length), 0, sizeof(int));
                        ms.Write(rij.IV, 0, rij.IV.Length);
                        using (var cs = new CryptoStream(ms, enc, CryptoStreamMode.Write))
                        {
                            using (var writer = new StreamWriter(cs))
                            {
                                writer.Write(data);
                            }
                        }

                        return Convert.ToBase64String(ms.ToArray());
                    }
                }
            }
        }

        private string Decrypt(string data)
        {
            var key = new Rfc2898DeriveBytes(EncPassPhrase, Encoding.UTF8.GetBytes(EncSalt));
            var bytes = Convert.FromBase64String(data);
            using (var ms = new MemoryStream(bytes))
            {
                using (var rij = new RijndaelManaged())
                {
                    rij.Key = key.GetBytes(rij.KeySize / 8);
                    rij.IV = ReadByteArray(ms);
                    using (var dec = rij.CreateDecryptor(rij.Key, rij.IV))
                    {
                        using (var cs = new CryptoStream(ms, dec, CryptoStreamMode.Read))
                        {
                            using (var sr = new StreamReader(cs))
                            {
                                return sr.ReadToEnd();
                            }
                        }
                    }
                }
            }
        }

        private static byte[] ReadByteArray(Stream s)
        {
            var rawLength = new byte[sizeof(int)];
            if (s.Read(rawLength, 0, rawLength.Length) != rawLength.Length)
            {
                throw new SystemException("Stream did not contain properly formatted byte array");
            }

            var buffer = new byte[BitConverter.ToInt32(rawLength, 0)];
            if (s.Read(buffer, 0, buffer.Length) != buffer.Length)
            {
                throw new SystemException("Did not read byte array properly");
            }

            return buffer;
        }
        #endregion
    }
}
