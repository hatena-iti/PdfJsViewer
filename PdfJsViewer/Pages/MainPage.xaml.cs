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
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
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
    public sealed partial class MainPage : Common.LayoutAwarePage
    {
        public MainPage()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// Invoked when this page is about to be displayed in a Frame.
        /// </summary>
        /// <param name="e">Event data that describes how this page was reached.  The Parameter
        /// property is typically used to configure the page.</param>
        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // Copy sample pdf files to Application local data directory
            await CopySamplePdfFilesAsync();
        }

        private void ShowPdf(object sender, RoutedEventArgs e)
        {
            if (this.Frame != null)
            {
                // Set the PDF file name as a parameter.
                // (Choose the PDF file in the Application local data directory. Set the file name with directory name.)
                if (!this.Frame.Navigate(typeof(PdfPage), "pdf\\mysample.pdf"))
                {
                    // Failed
                    System.Diagnostics.Debug.WriteLine("Navigate failed.");
                }
            }
        }

        private async Task CopySamplePdfFilesAsync()
        {
            try
            {
                var fromFolder = await Windows.ApplicationModel.Package.Current.InstalledLocation.GetFolderAsync("Assets");
                var toFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync("pdf", CreationCollisionOption.OpenIfExists);

                var files = await fromFolder.GetFilesAsync();
                foreach (var file in files)
                {
                    if (file.FileType == ".pdf" || file.FileType == ".PDF")
                    {
                        await file.CopyAsync(toFolder, file.Name, NameCollisionOption.ReplaceExisting);
                    }
                }
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine(e.Message);
            }
        }
    }
}
