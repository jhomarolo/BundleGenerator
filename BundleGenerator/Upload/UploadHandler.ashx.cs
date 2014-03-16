using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Script.Serialization;
using System;

namespace BundleGenerator.Upload
{
    /// <summary>
    /// Summary description for UploadHandler
    /// </summary>
    public class UploadHandler : IHttpHandler
    {
        private readonly JavaScriptSerializer js;

        private string StorageRoot
        {
            get { return Path.Combine(System.Web.HttpContext.Current.Server.MapPath("~/Files/")); } //Path should! always end with '/'
        }

        public UploadHandler()
        {
            js = new JavaScriptSerializer();
            js.MaxJsonLength = 2097152;
        }

        public bool IsReusable { get { return false; } }

        public void ProcessRequest(HttpContext context)
        {
            context.Response.AddHeader("Pragma", "no-cache");
            context.Response.AddHeader("Cache-Control", "private, no-cache");

            HandleMethod(context);
        }

        // Handle request based on method
        private void HandleMethod(HttpContext context)
        {
            switch (context.Request.HttpMethod)
            {
                case "HEAD":
                case "GET":
                    if (GivenFilename(context)) DeliverFile(context);
                    else ListCurrentFiles(context);
                    break;

                case "POST":
                case "PUT":
                    UploadFile(context);
                    break;

                case "DELETE":
                    DeleteFile(context);
                    break;

                case "OPTIONS":
                    ReturnOptions(context);
                    break;

                default:
                    context.Response.ClearHeaders();
                    context.Response.StatusCode = 405;
                    break;
            }
        }

        private static void ReturnOptions(HttpContext context)
        {
            context.Response.AddHeader("Allow", "DELETE,GET,HEAD,POST,PUT,OPTIONS");
            context.Response.StatusCode = 200;
        }

        // Delete file from the server
        private void DeleteFile(HttpContext context)
        {
            var filePath = StorageRoot + context.Request["f"];
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        // Upload file to the server
        private void UploadFile(HttpContext context)
        {
            var statuses = new List<FilesStatus>();
            var headers = context.Request.Headers;

            if (string.IsNullOrEmpty(headers["X-File-Name"]))
            {
                UploadWholeFile(context, statuses);
            }
            else
            {
                UploadPartialFile(headers["X-File-Name"], context, statuses);
            }

            WriteJsonIframeSafe(context, statuses);
        }

        // Upload partial file
        private void UploadPartialFile(string fileName, HttpContext context, List<FilesStatus> statuses)
        {
            if (context.Request.Files.Count != 1) throw new HttpRequestValidationException("Attempt to upload chunked file containing more than one fragment per request");
            var inputStream = context.Request.Files[0].InputStream;
            var fullName = StorageRoot + Path.GetFileName(fileName);

            using (var fs = new FileStream(fullName, FileMode.Append, FileAccess.Write))
            {
                var buffer = new byte[1024];

                var l = inputStream.Read(buffer, 0, 1024);
                while (l > 0)
                {
                    fs.Write(buffer, 0, l);
                    l = inputStream.Read(buffer, 0, 1024);
                }
                fs.Flush();
                fs.Close();
            }
            statuses.Add(new FilesStatus(new FileInfo(fullName)));
        }

        // Upload entire file
        private void UploadWholeFile(HttpContext context, List<FilesStatus> statuses)
        {
            StringBuilder stbCSS = new StringBuilder();
            StringBuilder stbJS = new StringBuilder();
            var ext = string.Empty;
            string guideCSS = string.Empty;
            string guideJS = string.Empty;
            int aptcss = 0;
            int aptjs = 0;

            for (int i = 0; i < context.Request.Files.Count; i++)
            {
                string filecomp = string.Empty;
                var file = context.Request.Files[i];
                string guid = Guid.NewGuid().ToString();
                var fullPath = StorageRoot + Path.GetFileName(guid + "$" + file.FileName);
                file.SaveAs(fullPath);
                byte[] filebyte = GetByteArquivo(file);
                ECMAScriptPacker p = new ECMAScriptPacker(0, true, true);
                filecomp = p.Pack(ByteToString(filebyte)).Replace("\n", "\r\n");
                System.IO.File.WriteAllText(fullPath, filecomp);
                string fullName = Path.GetFileName(guid + "$" + file.FileName);
                statuses.Add(new FilesStatus(fullName, filecomp.Length, fullPath));
                ext = Path.GetExtension(fullPath);
                if (IsJS(ext))
                {
                    stbJS.Append(filecomp);
                    if (aptjs == 0)
                    {
                        aptjs = 1;
                        guideJS = guid;
                    }
                }
                else if (IsCSS(ext))
                {
                    stbCSS.Append(filecomp);
                    if (aptcss == 0)
                    {
                        aptcss = 1;
                        guideCSS = guid;
                    }
                }
            }
            if (aptjs != 0)
            {
                string pathjs = StorageRoot + guideJS + "bundleJS.js";
                if (!File.Exists(pathjs))
                {
                    File.Create(pathjs).Dispose();
                }
                
                System.IO.File.WriteAllText(pathjs, stbJS.ToString());
            }
            if (aptcss != 0)
            {
                string pathcss = StorageRoot + guideCSS + "bundleCSS.js";
                File.Create(pathcss).Dispose();
                System.IO.File.WriteAllText(pathcss, stbCSS.ToString());
            }
           
        }

        private bool IsJS(string ext)
        {
            return ext == ".js";
        }
        private bool IsCSS(string ext)
        {
            return ext == ".css";
        }

        public static string ByteToString(byte[] bytes)
        {
            System.Text.UTF8Encoding enc = new System.Text.UTF8Encoding();
            return enc.GetString(bytes);
        }

        public static byte[] GetByteArquivo(HttpPostedFile file)
        {
            byte[] data = new byte[file.ContentLength];
            using (Stream inputStream = file.InputStream)
            {
                MemoryStream memoryStream = inputStream as MemoryStream;
                if (memoryStream == null)
                {
                    memoryStream = new MemoryStream();
                    inputStream.CopyTo(memoryStream);
                }

                data = memoryStream.ToArray();
            }

            return data;
        }

        private void WriteJsonIframeSafe(HttpContext context, List<FilesStatus> statuses)
        {
            context.Response.AddHeader("Vary", "Accept");
            try
            {
                if (context.Request["HTTP_ACCEPT"].Contains("application/json"))
                    context.Response.ContentType = "application/json";
                else
                    context.Response.ContentType = "text/plain";
            }
            catch
            {
                context.Response.ContentType = "text/plain";
            }

            var jsonObj = js.Serialize(statuses.ToArray());
            context.Response.Write(jsonObj);
        }

        private static bool GivenFilename(HttpContext context)
        {
            return !string.IsNullOrEmpty(context.Request["f"]);
        }

        private void DeliverFile(HttpContext context)
        {
            var filename = context.Request["f"];
            var filePath = StorageRoot + filename;

            if (File.Exists(filePath))
            {
                context.Response.AddHeader("Content-Disposition", "attachment; filename=\"" + filename.Substring(filename.IndexOf("$") + 1) + "\"");
                context.Response.ContentType = "application/octet-stream";
                context.Response.ClearContent();
                context.Response.WriteFile(filePath);
            }
            else
                context.Response.StatusCode = 404;
        }

        private void ListCurrentFiles(HttpContext context)
        {
            var files =
                new DirectoryInfo(StorageRoot)
                    .GetFiles("*", SearchOption.TopDirectoryOnly)
                    .Where(f => !f.Attributes.HasFlag(FileAttributes.Hidden))
                    .Select(f => new FilesStatus(f))
                    .ToArray();

            string jsonObj = js.Serialize(files);
            context.Response.AddHeader("Content-Disposition", "inline; filename=\"files.json\"");
            context.Response.Write(jsonObj);
            context.Response.ContentType = "application/json";
        }

    }
}