using System.Collections.Generic;
using NAudio.Wave;

namespace VOX2FTP
{
    static class AudioUtils
    {
        public static Dictionary<int, string> GetAudioDevices()
        {
            var ret = new Dictionary<int, string>();

            for (int devId = 0; devId < WaveIn.DeviceCount; devId++)
            {
                var deviceInfo = WaveIn.GetCapabilities(devId);
                ret.Add(devId, deviceInfo.ProductName);
            }

            return ret;
        }
    }
}
