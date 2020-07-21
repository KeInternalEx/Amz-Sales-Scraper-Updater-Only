using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace sdagger_auto_updater
{
    class ServiceLogger
    {
        private readonly string LogFileName;
        private readonly string LoggingDirectory;

        public void WriteLog(string Log)
        {
            string Prefix = "[" + DateTime.Now.ToString() + "]";
            string NewLinePrefix = new string(' ', Prefix.Length);
            string[] Lines = Log.Split('\n');
            string FileOutput = "";

            for (int i = 0; i < Lines.Length; i++)
            {
                string ConsoleOutput = (i == 0 ? Prefix : NewLinePrefix) + ' ' + Lines[i];

                FileOutput += ConsoleOutput + '\n';

                Console.WriteLine(ConsoleOutput);
            }

            if (!Directory.Exists(this.LoggingDirectory))
                Directory.CreateDirectory(this.LoggingDirectory);

            File.WriteAllText(this.LoggingDirectory + '/' + this.LogFileName, FileOutput);
        }

        public ServiceLogger(string FileName)
        {
            this.LogFileName = FileName;
            this.LoggingDirectory = Directory.GetCurrentDirectory() + "/logs";
        }
    }
}
