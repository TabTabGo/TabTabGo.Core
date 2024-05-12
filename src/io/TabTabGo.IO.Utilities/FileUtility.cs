using System.Text;

namespace TabTabGo.IO.Utilities;

public class FileUtility
{
    public static string GetTempFile(string tempPath, string extension)
        {
            var fileName = "ttg-" + Path.GetRandomFileName();
            if (extension.StartsWith("."))
            {
                return Path.Combine(tempPath, fileName + extension);
            }

            return Path.Combine(tempPath, fileName + "." + extension);
        }

        public static string CreateTempFile(string tempPath, string extension, byte[] content)
        {
            var tmpFilePath = GetTempFile(tempPath, extension);

            if (content != null)
            {
                File.WriteAllBytes(tmpFilePath, content);
            }

            return tmpFilePath;
        }

        [System.Obsolete("This method generates a temp file with pdf as an extension. Use the CreateTempFile override which accepts a file extension instead.")]
        public static string CreateTempFile(string tempPath, byte[] content)
        {
            var tmpFilePath = GetTempFile(tempPath, "pdf");

            if (content != null)
            {
                File.WriteAllBytes(tmpFilePath, content);
            }

            return tmpFilePath;
        }

        public static void DeleteFileIfExists(string filePath)
        {
            if (filePath != null && File.Exists(filePath))
                try
                {
                    File.Delete(filePath);
                }
                catch { }
        }

        public static byte[] GetFileContent(string filePath)
        {
            return File.ReadAllBytes(filePath);
        }

        public static async Task<byte[]> GetFileContentAsync(string filePath)
        {
            using (var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
            {
                byte[] buff = new byte[file.Length];
                await file.ReadAsync(buff, 0, (int)file.Length);

                return buff;
            }
        }

        public static void WriteFileContent(string fileContent, string filePath, bool createDirectory)
        {
            MemoryStream ms = new MemoryStream();
            StreamWriter sw = new StreamWriter(ms, Encoding.UTF8);

            sw.Write(fileContent);
            sw.Flush();

            FileUtility.WriteFileContent(ms.ToArray(), filePath, createDirectory);
        }

        public static void WriteFileContent(byte[] fileContent, string filePath, bool createDirectory)
        {
            if (createDirectory)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            }

            File.WriteAllBytes(filePath, fileContent);
        }

        public static async Task WriteFileContentAsync(byte[] fileContent, string filePath, bool createDirectory)
        {
            if (createDirectory)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            }

            using (var file = File.Create(filePath))
            {
                await file.WriteAsync(fileContent, 0, fileContent.Length);
            }
        }

        public static Task DeleteAsync(string filePath)
        {
            return Task.Factory.StartNew(() =>
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            });
        }

        /// <summary>
        /// determins that the filename will be unique (will not exist already) in the given path when created
        /// if a file already exists with the same name, it will keep adding an integer prefix to the name
        /// to make it unique, untill eather the filename is unique or counterLimit is reached.
        /// new recommended unique filename will be returned in the second component of the Tuple
        /// if it couldn't make a unique filename before the counterLimit is reached or encounters other error,
        /// it will return false in the first component of the Tuple and a message explaining the situation in the 3rd component of the tupple
        /// </summary>
        /// <param name="path"></param>
        /// <param name="filename"></param>
        /// <param name="counterLimit"></param>
        /// <returns>Tupple&lt;success,newfilename,message&gt;</returns>
        public static async Task<Tuple<bool, string, string>> EnsureUniqueFilename(string path, string filename, int counterLimit = 0)
        {
            bool foundUnique = false;
            int counter = 0;
            string currentFilename = filename;
            string msg = string.Empty;
            //split the filename into name and extension parts
            string nameWithoutExtension = Path.GetFileNameWithoutExtension(filename);
            string extension = Path.GetExtension(filename);
            try
            {
                //counterLimit 0 means go on forever
                while (!foundUnique && (counterLimit == 0 || counter >= counterLimit))
                {
                    if (!File.Exists(Path.Combine(path, currentFilename)))
                        foundUnique = true;
                    else
                    {
                        if (counter == int.MaxValue)//very strange if this happens
                        {
                            msg = $@"Couldn't get filename {filename} to be unqieur, max integer limit reached ({int.MaxValue})";
                            break;
                        }
                        counter++;
                        currentFilename = $@"{nameWithoutExtension}{counter}{extension}";
                    }
                }
            }
            catch (Exception x)
            {
                msg = $@"Couldn't get filename {filename} to be unique, {x.Message}";
                //TTGLog.Log(TTG.Core.LogLevel.Error, ARCHIVE_LOG_MODULE, msg, null, null, x);
            }

            if (!foundUnique)
            {
                //TTGLog.Log(TTG.Core.LogLevel.Error, ARCHIVE_LOG_MODULE, msg);
            }
            else
            {
                if (counter > 0)
                {
                    msg = $@"Unique filename {currentFilename} was reached after {counter} tries.";
                    //TTGLog.Log(TTG.Core.LogLevel.Trace, ARCHIVE_LOG_MODULE, msg);
                }
            }

            Tuple<bool, string, string> result = new Tuple<bool, string, string>(foundUnique, currentFilename, msg);

            return result;
        }
}