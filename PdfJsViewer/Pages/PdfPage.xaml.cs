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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;
using Windows.Storage.Streams;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

//The blank page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

namespace PdfJsViewer.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class PdfPage : Common.LayoutAwarePage
    {
        // In-app HTTP server instance
        private HttpServer.HttpServer httpServer;

        // PDF viewer contents folder in the application local data folder
        private const string LOCAL_CONTENTS_FOLDER = "web";

        // original PDF viewer contents folder in the application project folder
        private const string ORIGINAL_CONTENTS_FOLDER = "Assets\\web";

        public PdfPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = Windows.UI.Xaml.Navigation.NavigationCacheMode.Enabled;
        }

        /// <summary>
        /// Invoked when this page is about to be displayed in a Frame.
        /// </summary>
        /// <param name="e">Event data that describes how this page was reached.  The Parameter
        /// property is typically used to configure the page.</param>
        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // Start the in-app HTTP server
            httpServer = HttpServer.HttpServer.Instance;

            // Copy the PDF viewer contents file to the application local data folder.
            await PrepareContentsAsync();

            // Allow "window.external.notify()" in this webview.
            MyWebView.ScriptNotify += MyWebView_ScriptNotify;
            MyWebView.AllowedScriptNotifyUris = WebView.AnyScriptNotifyUri;

            string parameter = e.Parameter.ToString();
            string path = "";
            if (parameter != null)
            {
                path = "/" + parameter.Replace("\\", "/");
            }

            var port = httpServer.Port;

            // Open the pdf viewer in In-app HTTP server. (Set the PDF file name to "file" parameter.)
            if (path != "")
            {
                MyWebView.Navigate(new Uri("http://localhost:" + port + "/" + LOCAL_CONTENTS_FOLDER + "/viewer.html?file=" + path));
            }
            else
            {
                MyWebView.Navigate(new Uri("http://localhost:" + port + "/" + LOCAL_CONTENTS_FOLDER + "/viewer.html"));
            }
        }

        private void MyWebView_ScriptNotify(object sender, NotifyEventArgs e)
        {
            //throw new NotImplementedException();
            HandleScriptNotify(e.Value);
        }

        private void HandleScriptNotify(string value)
        {
            try
            {
                JsonObject json = JsonValue.Parse(value).GetObject();

                var action = json["action"].GetString();
                if (action == "log")
                {
                    // log
                    Log(json);
                }
                else if (action == "save")
                {
                    // save as file
                    SaveFileAsync(json);
                }
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine(e.Message);
            }
        }

        private void Log(JsonObject json)
        {
            // json is:
            // {
            //   "action" :"log",
            //   "message":"some message"
            // }

            var message = json["message"].GetString();
            System.Diagnostics.Debug.WriteLine(message);
        }

        private async Task SaveFileAsync(JsonObject json)
        {
            // json is:
            // {
            //   "action":"save"
            //   "path"  :"saved file path (/mysample.pdf etc.)"
            // }

            var path = json["path"].GetString();
            var originalFile = await httpServer.GetFileAsync(httpServer.SplitToPath(path));
            var extension = Path.GetExtension(path);

            if (EnsureUnsnapped())
            {
                FileSavePicker savePicker = new FileSavePicker();

                savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

                // Dropdown of file types the user can save the file as
                savePicker.FileTypeChoices.Add("Document", new List<string>() { extension });

                // Default file name if the user does not type one in or select a file to replace
                savePicker.SuggestedFileName = originalFile.Name;

                StorageFile file = await savePicker.PickSaveFileAsync();
                if (file != null)
                {
                    // Prevent updates to the remote version of the file until we finish making changes and call CompleteUpdatesAsync.
                    CachedFileManager.DeferUpdates(file);

                    // read buffer from original file
                    var buffer = await FileIO.ReadBufferAsync(originalFile);
                    // write to file
                    await FileIO.WriteBufferAsync(file, buffer);

                    // Let Windows know that we're finished changing the file so the other app can update the remote version of the file.
                    // Completing updates may require Windows to ask for user input.
                    FileUpdateStatus status = await CachedFileManager.CompleteUpdatesAsync(file);
                    if (status == FileUpdateStatus.Complete)
                    {
                        // Success
                    }
                    else
                    {
                        // Failed
                    }
                }
                else
                {
                    // Cancelled
                }
            }
        }

        private bool EnsureUnsnapped()
        {
            // FilePicker APIs will not work if the application is in a snapped state.
            // If an app wants to show a FilePicker while snapped, it must attempt to unsnap first
            return ((ApplicationView.Value != ApplicationViewState.Snapped) || ApplicationView.TryUnsnap());
        }

        /// <summary>
        /// Copy the PDF viewer contents file to the application local data folder.
        /// </summary>
        /// <returns></returns>
        private async Task PrepareContentsAsync()
        {
            if (!await IsFoundNewContents())
            {
                return;
            }

            try
            {
                var appInstallFolder = Windows.ApplicationModel.Package.Current.InstalledLocation;
                var originalWebContentsFolder = await appInstallFolder.GetFolderAsync(ORIGINAL_CONTENTS_FOLDER);

                await CopyWebViewerFilesAsync(originalWebContentsFolder, originalWebContentsFolder.Name);
            }
            catch (FileNotFoundException e)
            {
                System.Diagnostics.Debug.WriteLine(e.Message);
            }
        }

        private async Task<bool> IsFoundNewContents()
        {
            bool result = false;

            string oldVersion = "";
            string newVersion = "999";

            const string VERSION_FILE = "version.txt";

            try
            {
                // Check the old and new "version.txt"

                var webContentsFolder = await ApplicationData.Current.LocalFolder.GetFolderAsync(LOCAL_CONTENTS_FOLDER);
                var oldVersionFile = await webContentsFolder.GetFileAsync(VERSION_FILE);
                oldVersion = await FileIO.ReadTextAsync(oldVersionFile);

                var appInstallFolder = Windows.ApplicationModel.Package.Current.InstalledLocation;
                var originalWebContentsFolder = await appInstallFolder.GetFolderAsync(ORIGINAL_CONTENTS_FOLDER);
                var newVersionFile = await originalWebContentsFolder.GetFileAsync(VERSION_FILE);
                newVersion = await FileIO.ReadTextAsync(newVersionFile);
            }
            catch (Exception e)
            {
            }

            if (newVersion.CompareTo(oldVersion) > 0)
            {
                System.Diagnostics.Debug.WriteLine("New version web contens were found.");
                result = true;
            }

            return result;
        }

        private async Task CopyWebViewerFilesAsync(IStorageFolder fromFolder, string fromFolderName)
        {
            var toFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(fromFolderName, 
                                                                        CreationCollisionOption.OpenIfExists);

            var folders = await fromFolder.GetFoldersAsync();
            foreach (var folder in folders)
            {
                await CopyWebViewerFilesAsync(folder, fromFolderName + "\\" + folder.Name);
            }

            var files = await fromFolder.GetFilesAsync();
            foreach (var file in files)
            {
                await file.CopyAsync(toFolder, file.Name, NameCollisionOption.ReplaceExisting);
            }
        }
    }
}
