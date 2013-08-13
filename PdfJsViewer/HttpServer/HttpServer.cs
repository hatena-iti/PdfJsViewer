/* Copyright 2013 Intelligent Technology Inc.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using System.IO;
using Windows.Storage;
using Windows.System.Threading;
using Windows.Foundation;

using Windows.UI.Popups;
using System.Diagnostics;

namespace PdfJsViewer.HttpServer
{
    /// <summary>
    /// In-app HTTP server (Singleton class)
    /// </summary>
    public class HttpServer
    {
        private static readonly HttpServer _instance = new HttpServer();

        public static HttpServer Instance { get { return _instance; } }

        /// <summary>
        /// Http server port number
        /// </summary>
        public int Port { get; private set; }

        // The default port to listen on
        private const int DEFAULT_PORT = 8088;

        // a socket listener instance
        private readonly StreamSocketListener _listener;

        // Document root folder
        private StorageFolder _rootFolder;

        //IAsyncAction ServerAction;

        private IDictionary<string, string> _contentTypes;

        /// <summary>
        /// create an instance of a HttpService class.
        /// </summary>
        private HttpServer()
        {
            _contentTypes = new Dictionary<string, string> {
                {".html", "text/html"}, {".htm", "text/html"}, {".js", "text/javascript"}, {".css", "text/css"},
                {".png", "image/png"}, {".jpeg", "image/jpeg"}, {".jpg", "image/jpeg"}, 
                {".gif", "image/gif"}, {".bmp", "image/bmp"}, 
                {".pdf", "application/pdf"}, {".properties", "application/l10n"}
            };
            _listener = new StreamSocketListener();

            //_rootFolder = Windows.ApplicationModel.Package.Current.InstalledLocation; // Application install directory
            _rootFolder = ApplicationData.Current.LocalFolder; // Application data directory

            // start the service listening
            StartService();
        }

        /// <summary>
        /// Start the HTTP Server
        /// </summary>
        private async void StartService()
        {
            // when a connection is recieved, process
            // the request.
            _listener.ConnectionReceived += (s, e) =>
            {
                    ProcessRequestAsync(e.Socket);
            };


            int port = DEFAULT_PORT;
            while (true)
            {
                try
                {
                    // Bind the service to the default port.
                    await _listener.BindServiceNameAsync(port.ToString());
                    Debug.WriteLine("start http server on {0}", port);
                    this.Port = port;
                    break;
                }
                catch (Exception)
                {
                    Debug.WriteLine("can't bind port {0}. try next", port);
                    port++;
                }
            }
        }

        /// <summary>
        /// When a connection is recieved, process the request.
        /// </summary>
        /// <param name="socket">the incoming socket connection.</param>
        private async void ProcessRequestAsync(StreamSocket socket)
        {
            StringBuilder inputRequestBuilder = new StringBuilder();

            // Read all the request data.
            // (This is assuming it is all text data of course)
            using (var stream = socket.InputStream.AsStreamForRead())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                var line = "start";
                while ((line = await reader.ReadLineAsync()) != "") {
                    inputRequestBuilder.AppendLine(line);
                }
            }

            using (var output = socket.OutputStream)
            {
                // extract the request string.
                var request = inputRequestBuilder.ToString();
                var requestMethod = request.Split('\n')[0];
                var requestParts = requestMethod.Split(' ');

                if (requestParts[0].CompareTo("GET") == 0)
                {
                    // process the request and write the response.
                    await WriteResponseAsync(requestParts[1], socket.OutputStream);
                }
            }
        }

        /// <summary>
        /// Write the HTTP response to the request out to the output
        /// stream on the socket.
        /// </summary>
        /// <param name="resourceName">The resource name to retrieve.</param>
        /// <param name="outputStream">The output stream to write to.</param>
        /// <returns>A task object.</returns>
        private async Task WriteResponseAsync(string resourceName, IOutputStream outputStream)
        {
            using (var writeStream = outputStream.AsStreamForWrite())
            {
                var resourceParts = resourceName.Split('?');
                var requestPath = resourceParts[0];

                // check the extension is supported.
                var extension = Path.GetExtension(requestPath);

                if (_contentTypes.ContainsKey(extension))
                {
                    try
                    {
                        string contentType = _contentTypes[extension];

                        // read the local data.

                        var requestedFile = await GetFileAsync(SplitToPath(requestPath));
                        var fileStream = await requestedFile.OpenReadAsync();
                        var size = fileStream.Size;

                        // write out the HTTP headers.
                        var header = String.Format("HTTP/1.1 200 OK\r\n" +
                                                 "Content-Type: {0}\r\n" +
                                                 "Content-Length: {1}\r\n" +
                                                 "Connection: close\r\n" +
                                                 "\r\n",
                                                 contentType,
                                                 fileStream.Size);

                        var headerArray = Encoding.UTF8.GetBytes(header);

                        await writeStream.WriteAsync(headerArray, 0, headerArray.Length);

                        // copy the requested file to the output stream.
                        await fileStream.AsStreamForRead().CopyToAsync(writeStream);
                    } catch (Exception e) {
                        Debug.WriteLine("e:{0}", e);
                        SendNotFound(writeStream);
                    }
                }
                else
                {
                    // unrecognised file type.
                    // handle as 404 not found.

                    SendNotFound(writeStream);
                }
                await writeStream.FlushAsync();
            }
        }

        private async void SendNotFound(Stream writeStream)
        {
            var header = "HTTP/1.1 404 Not Found\r\n" +
                         "Connection: close\r\n" +
                         "Content-Type: text/html\r\n" +
                         "Content-Length: 20\r\n" + 
                         "\r\n" +
                         "<body>404 Not Found.</body>\r\n\r\n";
            var headerArray = Encoding.UTF8.GetBytes(header);
            await writeStream.WriteAsync(headerArray, 0, headerArray.Length);
            await writeStream.FlushAsync();
        }
        
        public List<string> SplitToPath(string path)
        {
            return Array.FindAll(path.Split('/'), name => name.Length > 0).ToList();
        }

        public async Task<IStorageFile> GetFileAsync(List<string> paths, IStorageFolder folder = null)
        {
            var dir = folder ?? _rootFolder;

            var contentDir = await paths.Take(paths.Count - 1).Aggregate<string, Task<IStorageFolder>>(Task.Run(() => dir), async (d, name) =>
            {
                return await d.Result.GetFolderAsync(name);
            });
            return await contentDir.GetFileAsync(paths.Last());
        }
    }
}
