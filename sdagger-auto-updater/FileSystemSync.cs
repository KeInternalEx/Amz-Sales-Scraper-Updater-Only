using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace sdagger_auto_updater
{

    class FileSystemSync
    {
        private struct FileBinding
        {
            public string[] Files;
            public string Token;
        }

        private readonly Dictionary<string, FileBinding> Bindings;
        private readonly string MasterDirectory;
        private readonly string OutputDirectory;

        public FileSystemSync(string MasterDirectory, string OutputDirectory)
        {
            this.MasterDirectory = MasterDirectory;
            this.OutputDirectory = OutputDirectory;

            this.Bindings = new Dictionary<string, FileBinding>();
        }


        public void AddBinding(string BindingName, string Token, params string[] FileNames) =>
            this.Bindings.Add(BindingName, new FileBinding
            {
                Files = FileNames,
                Token = Token
            });

        public void RemoveBinding(string BindingName) =>
            this.Bindings.Remove(BindingName);

        private void SyncFileChanges(string FilePath, List<(string, string)> Changes)
        {
            if (File.Exists(FilePath))
            {
                string FileContent = File.ReadAllText(FilePath);

                foreach ((string, string) Change in Changes)
                {
                    FileContent.Replace(
                        Change.Item1, // token
                        Change.Item2); // value
                }

                File.WriteAllText(FilePath, FileContent);
            }
        }

        public void SyncChanges(params (string, string)[] Changes)
        {
            Dictionary<string, List<(string, string)>> ChangesByFile =
                new Dictionary<string, List<(string, string)>>();

        // Sort changes by file
            foreach((string, string) Change in Changes)
            {
                FileBinding Binding;
                if (this.Bindings.TryGetValue(Change.Item1, out Binding))
                {
                    foreach (string FileName in Binding.Files)
                    {
                        if (!ChangesByFile.ContainsKey(FileName))
                            ChangesByFile[FileName] = new List<(string, string)>();

                        ChangesByFile[FileName].Add((Binding.Token, Change.Item2));
                    }
                }
            }

        // Move master copy to output directory
            FileSystem.DeleteDirectory(this.OutputDirectory, DeleteDirectoryOption.DeleteAllContents);
            FileSystem.CopyDirectory(this.MasterDirectory, this.OutputDirectory);

        // Make file changes
            foreach (KeyValuePair<string, List<(string, string)>> ChangesForFile in ChangesByFile)
            {
                this.SyncFileChanges(
                    this.OutputDirectory + ChangesForFile.Key, // File name
                    ChangesForFile.Value); // List of changes by token
            }
        }
    }
}
