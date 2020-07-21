#define DEBUG_BUILD


using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Permissions;
using System.Text;

namespace sdagger_auto_updater
{

    class Program
    {


    // extension ID and password are already hardcoded into this executable
    // keep master copy of extension in working directory
    // keep running copy of extension in working directory

    // overlay data:
    // 00: keySize [1 b]
    // 01: startOfKey [keySize b]
    // 01 + keySize: startOfBlob [eof - keySize b]
    // eof

        static readonly string SDaggerPublicKey = ""; // TODO: Base64'd public key


        static byte[] CopyEmbeddedDataToMemory() // CAN THROW
        {
            string SelfPath = Assembly.GetEntryAssembly().Location;
            byte[] FileBuffer = File.ReadAllBytes(SelfPath);
            PeParser Parser = new PeParser(FileBuffer);
            int OverlayOffset = checked((int)Parser.GetOverlayOffset());

            return FileBuffer
                .Skip(OverlayOffset)
                .Take(FileBuffer.Length - OverlayOffset)
                .ToArray();
        }

        static string DecryptEmbeddedData(byte[] ExtractedData)
        {
            int EncryptedKeySize = (int)ExtractedData[0];

            IEnumerable<byte> EncryptedKey = ExtractedData
                .Skip(1)
                .Take(EncryptedKeySize);

            IEnumerable<byte> EncryptedData = ExtractedData
                .Skip(1 + EncryptedKeySize)
                .Take(ExtractedData.Length - EncryptedKeySize - 1);

            using (RSACryptoServiceProvider RsaProvider = new RSACryptoServiceProvider()) {

                RsaProvider.ImportCspBlob(
                    Convert.FromBase64String(SDaggerPublicKey));

                byte[] DecryptedKey = RsaProvider.Decrypt(
                    EncryptedKey.ToArray(),
                    true);

                using (Aes AesProvider = Aes.Create()) {
                    AesProvider.Key = DecryptedKey.Take(16).ToArray(); // KEY IS ALWAYS 128 BIT
                    AesProvider.IV = DecryptedKey.Skip(16).Take(16).ToArray(); // IV IS ALWAYS 128 BIT

                    ICryptoTransform Decryptor = AesProvider.CreateDecryptor(
                        AesProvider.Key,
                        AesProvider.IV);

                    using (MemoryStream MsDecrypt = new MemoryStream(EncryptedData.ToArray()))
                    {
                        using (CryptoStream csDecrypt = new CryptoStream(MsDecrypt, Decryptor, CryptoStreamMode.Read))
                        {
                            using (StreamReader srDecrypt = new StreamReader(csDecrypt))
                            {
                                return srDecrypt.ToString();
                            }
                        }
                    }
                }
            }
        }

        static void UpdateAutorunKey(UpdaterConfiguration Config)
        {
            RegistryKey StartupKey =
                Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);

            StartupKey.SetValue(
                $"SDaggerUpdateService{Config.ExtensionId}",
                '"' + Assembly.GetEntryAssembly().Location + '"',
                RegistryValueKind.String);
        }

        static string ReadResource(string Name)
        {
            Assembly ExecutingAssembly = Assembly.GetExecutingAssembly();

            using (Stream Stream = ExecutingAssembly.GetManifestResourceStream(Name))
            using (StreamReader Reader = new StreamReader(Stream))
            {
                return Reader.ReadToEnd();
            }
        }
        static void CreateUninstaller(UpdaterConfiguration Config)
        {
            string CurrentDirectory = Directory.GetCurrentDirectory();
            string UninstallerPath = CurrentDirectory + "/uninstall.bat";

            if (File.Exists(UninstallerPath))
                File.Delete(UninstallerPath);

            string Uninstaller = ReadResource("uninstall.bat");
            Uninstaller = Uninstaller.Replace("[EXTENSION_ID]", Config.ExtensionId);

            File.WriteAllText(UninstallerPath, Uninstaller);
        }

        [PermissionSet(SecurityAction.Demand, Name = "FullTrust")]
        static void AppMain(string[] args)
        {
            ServiceLogger Logger = new ServiceLogger(DateTime.Now.ToString() + ".log");

            byte[] EncryptedConfig = CopyEmbeddedDataToMemory();
            string DecryptedConfig = Encoding.UTF8.GetString(EncryptedConfig); // todo: replace with DecryptEmbeddedData(EncryptedConfig);

            UpdaterConfiguration Config =
                JsonConvert.DeserializeObject<UpdaterConfiguration>(DecryptedConfig);

            UpdaterService Updater = new UpdaterService(Config, Logger);

#if DEBUG_BUILD
            Logger.WriteLog("Encrypted configuration data >\n" + BitConverter.ToString(EncryptedConfig).Replace("-", " "));
            Logger.WriteLog("Decrypted configuration data >\n" + DecryptedConfig);
#endif

            Console.Title = "[SELLER BEAST] " + Config.StoreName + " | " + Config.CompanyId;

            UpdateAutorunKey(Config);
            CreateUninstaller(Config);

            Updater.RunTimers();

        // todo: catch closes and ask if they really want to close the application.


        }

        static void Main(string[] args)
        {
            AppMain(args);
            Console.Read();
        }
    }
}
