﻿using GoogleAnalytics;
using Microsoft.QueryStringDotNET;
using Newtonsoft.Json;
using QuickShare.Common;
using QuickShare.DataStore;
using QuickShare.FileTransfer;
using QuickShare.Classes;
using QuickShare.TextTransfer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.AppService;
using Windows.ApplicationModel.Background;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.ShareTarget;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI.Popups;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using QuickShare.HelperClasses;
using QuickShare.ViewModels.ShareTarget;
using QuickShare.HelperClasses.Version;
using Microsoft.Services.Store.Engagement;

namespace QuickShare
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    sealed partial class App : Application
    {
#if !DEBUG
        public static Tracker Tracker;
#endif
        public static DateTime? LaunchTime { get; set; }

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
            this.Suspending += OnSuspending;
            this.UnhandledException += App_UnhandledException;

            LaunchTime = DateTime.Now;

            UWP.Rome.RomePackageManager.Instance.Initialize("com.roamit.service");
            DataStore.DataStorageProviders.Init(Windows.Storage.ApplicationData.Current.LocalFolder.Path);

#if !DEBUG
            AnalyticsManager.Current.IsDebug = false; //use only for debugging, returns detailed info on hits sent to analytics servers
            AnalyticsManager.Current.ReportUncaughtExceptions = true; //catch unhandled exceptions and send the details
            AnalyticsManager.Current.AutoAppLifetimeMonitoring = true; //handle suspend/resume and empty hit batched hits on suspend

            Tracker = AnalyticsManager.Current.CreateTracker(Common.Secrets.GoogleAnalyticsId);
            Tracker.AppName = "Roamit-Windows10";
#endif
        }

        private void App_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            LogExceptionMessage(e.Message + "\r\n\r\n" + e.Exception.ToString());
        }

        private async void LogExceptionMessage(string msg)
        {
            try
            {
                string appIdentity = $"({DeviceInfo.ApplicationName} {DeviceInfo.ApplicationVersionString} - {DeviceInfo.SystemArchitecture} {DeviceInfo.SystemFamily} {DeviceInfo.SystemVersion})\n";
                var message = new MessageDialog(appIdentity + msg, "Unhandled exception occured.");
                await message.ShowAsync();
            }
            catch { }
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used such as when the application is launched to open a specific file.
        /// </summary>
        /// <param name="e">Details about the launch request and process.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            if ((ApplicationData.Current.LocalSettings.Values.ContainsKey("FirstRun")) && (ApplicationData.Current.LocalSettings.Values["FirstRun"].ToString() == "false"))
            {
                InitApplication(e, typeof(MainPage));
            }
            else
            {
                LaunchTime = null;
                InitApplication(e, typeof(Intro));
            }
#if !DEBUG
            var osVersion = DeviceInfo.SystemVersion.ToString();
            App.Tracker.Send(HitBuilder.CreateCustomEvent("OSVersion", osVersion).Build());

            StoreServicesCustomEventLogger logger = StoreServicesCustomEventLogger.GetDefault();
            logger.Log("AppLaunched_" + osVersion);
#endif
        }

        private void InitApplication(LaunchActivatedEventArgs e, Type defaultPage)
        {
            Debug.WriteLine("Launched.");
#if DEBUG && false
            if (System.Diagnostics.Debugger.IsAttached)
            {
                this.DebugSettings.EnableFrameRateCounter = true;
            }
#endif
            Frame rootFrame = Window.Current.Content as Frame;

            // Do not repeat app initialization when the Window already has content,
            // just ensure that the window is active
            if (rootFrame == null)
            {
                // Create a Frame to act as the navigation context and navigate to the first page
                rootFrame = new Frame();

                rootFrame.NavigationFailed += OnNavigationFailed;

                if (e?.PreviousExecutionState == ApplicationExecutionState.Terminated)
                {
                    //TODO: Load state from previously suspended application
                }

                // Place the frame in the current Window
                Window.Current.Content = rootFrame;
            }

            if (e?.PrelaunchActivated != true)
            {
                if ((rootFrame.Content == null) && (defaultPage != null))
                {
                    // When the navigation stack isn't restored navigate to the first page,
                    // configuring the new page by passing required information as a navigation
                    // parameter
                    rootFrame.Navigate(defaultPage, e?.Arguments);
                }
                ApplicationView.GetForCurrentView().SetPreferredMinSize(new Size(330, 550));

                // Ensure the current window is active
                Window.Current.Activate();
            }
        }

        /// <summary>
        /// Invoked when Navigation to a certain page fails
        /// </summary>
        /// <param name="sender">The Frame which failed navigation</param>
        /// <param name="e">Details about the navigation failure</param>
        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName, e.Exception);
        }

        protected override async void OnActivated(IActivatedEventArgs e)
        {
            Debug.WriteLine("Activated.");

            Frame rootFrame = Window.Current.Content as Frame;

            bool isJustLaunched = (rootFrame == null);

            if (e is ToastNotificationActivatedEventArgs)
            {
                var toastActivationArgs = e as ToastNotificationActivatedEventArgs;

                // Parse the query string
                QueryString args = QueryString.Parse(toastActivationArgs.Argument);

                if (!args.Contains("action"))
                {
                    LaunchRootFrameIfNecessary(ref rootFrame, true);
                    return;
                }

                HistoryRow hr;
                switch (args["action"])
                {
                    case "cloudClipboard":
                        LaunchRootFrameIfNecessary(ref rootFrame, false);
                        rootFrame.Navigate(typeof(ClipboardReceive), "CLOUD_CLIPBOARD");
                        break;
                    case "clipboardReceive":
                        LaunchRootFrameIfNecessary(ref rootFrame, false);
                        rootFrame.Navigate(typeof(ClipboardReceive), args["guid"]);
                        break;
                    case "fileProgress":
                        LaunchRootFrameIfNecessary(ref rootFrame, false);
                        if (rootFrame.Content is MainPage)
                            break;
                        rootFrame.Navigate(typeof(MainPage));
                        break;
                    case "fileFinished":
                        LaunchRootFrameIfNecessary(ref rootFrame, false);
                        if (rootFrame.Content is MainPage)
                            break;
                        rootFrame.Navigate(typeof(MainPage), "history");
                        break;
                    case "openFolder":
                        hr = await GetHistoryItemGuid(Guid.Parse(args["guid"]));
                        await LaunchOperations.LaunchFolderFromPathAsync((hr.Data as ReceivedFileCollection).StoreRootPath);
                        if (isJustLaunched)
                            Application.Current.Exit();
                        break;
                    case "openFolderSingleFile":
                        hr = await GetHistoryItemGuid(Guid.Parse(args["guid"]));
                        await LaunchOperations.LaunchFolderFromPathAndSelectSingleItemAsync((hr.Data as ReceivedFileCollection).Files[0].StorePath, (hr.Data as ReceivedFileCollection).Files[0].Name);
                        if (isJustLaunched)
                            Application.Current.Exit();
                        break;
                    case "openSingleFile":
                        hr = await GetHistoryItemGuid(Guid.Parse(args["guid"]));
                        await LaunchOperations.LaunchFileFromPathAsync((hr.Data as ReceivedFileCollection).Files[0].StorePath, (hr.Data as ReceivedFileCollection).Files[0].Name);
                        if (isJustLaunched)
                            Application.Current.Exit();
                        break;
                    case "saveAsSingleFile":
                    case "saveAs":
                        LaunchRootFrameIfNecessary(ref rootFrame, false);
                        rootFrame.Navigate(typeof(ProgressPage));
                        var guid = Guid.Parse(args["guid"]);
                        await ReceivedSaveAsHelper.SaveAs(guid);
                        if ((isJustLaunched) || (DeviceInfo.FormFactorType != DeviceInfo.DeviceFormFactorType.Desktop))
                            Application.Current.Exit();
                        else
                            rootFrame.GoBack();
                        break;
                    default:
                        LaunchRootFrameIfNecessary(ref rootFrame, true);
                        break;
                }

            }
            else if (e.Kind == ActivationKind.Protocol)
            {
                ProtocolActivatedEventArgs pEventArgs = e as ProtocolActivatedEventArgs;

                if ((pEventArgs.Uri.AbsoluteUri.ToLower() == "roamit://wake") || (pEventArgs.Uri.AbsoluteUri.ToLower() == "roamit://wake/"))
                {
                    Debug.WriteLine("Wake request received");
                    Application.Current.Exit();
                }
                else
                {
                    string clipboardData = ParseFastClipboardUri(pEventArgs.Uri.AbsoluteUri);
                    string remoteLaunchUriData = ParseRemoteLaunchUri(pEventArgs.Uri.AbsoluteUri);
                    string localLaunchUriData = ParseLocalLaunchUri(pEventArgs.Uri.AbsoluteUri);
                    string commServiceData = ParseCommunicationServiceData(pEventArgs.Uri.AbsoluteUri);
                    bool isSettings = ParseSettings(pEventArgs.Uri.AbsoluteUri);
                    string receiveDialogData = ParseReceive(pEventArgs.Uri.AbsoluteUri);
                    

                    if (isSettings)
                    {
                        if (rootFrame == null)
                        {
                            LaunchRootFrameIfNecessary(ref rootFrame, false);
                        }
                        rootFrame.Navigate(typeof(MainPage), "settings");
                    }
                    else if (receiveDialogData.Length > 0)
                    {
                        if (rootFrame == null)
                        {
                            LaunchRootFrameIfNecessary(ref rootFrame, false);
                        }
                        rootFrame.Navigate(typeof(MainPage), "receiveDialog");

                        if (receiveDialogData.Length > 1)
                        {
                            var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(receiveDialogData.Substring(1).DecodeBase64());
                            await ParseMessage(data);
                        }
                    }
                    else if (commServiceData.Length > 0)
                    {
                        var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(commServiceData.DecodeBase64());
                        await ParseMessage(data);
                    }
                    else if (clipboardData.Length > 0)
                    {
                        string[] parts = clipboardData.Split('?');
                        var guid = await TextReceiver.QuickTextReceivedAsync(parts[0].DecodeBase64(), parts[1].DecodeBase64());

                        LaunchRootFrameIfNecessary(ref rootFrame, false);
                        rootFrame.Navigate(typeof(ClipboardReceive), guid.ToString());
                    }
                    else if (remoteLaunchUriData.Length > 0)
                    {
#if !DEBUG
                        App.Tracker.Send(HitBuilder.CreateCustomEvent("ExtensionCalled", "").Build());
#endif

                        string type = ExternalContentHelper.SetUriData(new Uri(remoteLaunchUriData.DecodeBase64()));

                        SendDataTemporaryStorage.IsSharingTarget = true;

                        if (rootFrame == null)
                        {
                            LaunchRootFrameIfNecessary(ref rootFrame, false);
                            rootFrame.Navigate(typeof(MainPage), new ShareTargetDetails
                            {
                                Type = type,
                            });
                        }
                        else
                        {
                            MainPage.Current.BeTheShareTarget(new ShareTargetDetails
                            {
                                Type = type,
                            });
                        }
                    }
                    else if (localLaunchUriData.Length > 0)
                    {
                        try
                        {
                            //TODO: Log it in history
                            await LaunchOperations.LaunchUrl(localLaunchUriData.DecodeBase64());
                        }
                        catch
                        {
                        }

                        if (rootFrame == null)
                            Application.Current.Exit();
                    }
                    else
                    {
                        LaunchRootFrameIfNecessary(ref rootFrame, true);
                    }
                }
            }
            else
            {
                LaunchRootFrameIfNecessary(ref rootFrame, true);
            }

            base.OnActivated(e);
        }

        private bool ParseSettings(string s)
        {
            s = s.ToLower();

            if ((s == "roamit://settings") || (s == "roamit://settings/"))
                return true;
            return false;
        }

        private string ParseReceive(string s)
        {
            s = s.ToLower();

            if (s.Length < 22)
                return "";

            var part = s.Substring(0, 22);

            if (part == "roamit://receivedialog")
            {
                if (s.Length > 23)
                    return "/" + s.Substring(23);
                else
                    return "/";
            }
            return "";
        }

        private static string GetUrlDataPart(string url, string firstPart)
        {
            if (url.Length < firstPart.Length)
                return "";

            var command = url.Substring(0, firstPart.Length).ToLower();

            return (command == firstPart) ? url.Substring(firstPart.Length) : "";
        }

        private string ParseFastClipboardUri(string s)
        {
            return GetUrlDataPart(s, "roamit://clipboard/");
        }

        private string ParseRemoteLaunchUri(string s)
        {
            return GetUrlDataPart(s, "roamit://remotelaunch/");
        }

        private string ParseLocalLaunchUri(string s)
        {
            return GetUrlDataPart(s, "roamit://url/");
        }

        private string ParseCommunicationServiceData(string s)
        {
            return GetUrlDataPart(s, "roamit://comm/");
        }

        private async Task<HistoryRow> GetHistoryItemGuid(Guid guid)
        {
            HistoryRow hr;
            await DataStorageProviders.HistoryManager.OpenAsync();
            hr = DataStorageProviders.HistoryManager.GetItem(guid);
            DataStorageProviders.HistoryManager.Close();
            return hr;
        }

        private void LaunchRootFrameIfNecessary(ref Frame rootFrame, bool launchMainPage)
        {
            LaunchTime = null;
            if (rootFrame == null)
            {
                InitApplication(null, launchMainPage ? typeof(MainPage) : null);
                rootFrame = Window.Current.Content as Frame;
            }
        }

        /// <summary>
        /// Invoked when application execution is being suspended.  Application state is saved
        /// without knowing whether the application will be terminated or resumed with the contents
        /// of memory still intact.
        /// </summary>
        /// <param name="sender">The source of the suspend request.</param>
        /// <param name="e">Details about the suspend request.</param>
        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            //TODO: Save application state and stop any background activity

            deferral.Complete();
        }

        private AppServiceConnection notificationAppServiceConnection;
        private BackgroundTaskDeferral notificationAppServiceDeferral;

        private AppServiceConnection messageCarrierAppServiceConnection;
        private BackgroundTaskDeferral messageCarrierAppServiceDeferral;

        private AppServiceConnection pcAppServiceConnection;
        private BackgroundTaskDeferral pcAppServiceDeferral;

        private AppServiceConnection communicationServiceConnection;
        private BackgroundTaskDeferral communicationServiceDeferral;

        protected override void OnBackgroundActivated(BackgroundActivatedEventArgs args)
        {
            base.OnBackgroundActivated(args);

            IBackgroundTaskInstance taskInstance = args.TaskInstance;
            AppServiceTriggerDetails appService = taskInstance.TriggerDetails as AppServiceTriggerDetails;

            if (appService?.Name == "com.roamit.notificationservice")
            {
                notificationAppServiceDeferral = taskInstance.GetDeferral();
                taskInstance.Canceled -= OnNotificationAppServicesCanceled;
                taskInstance.Canceled += OnNotificationAppServicesCanceled;
                notificationAppServiceConnection = appService.AppServiceConnection;
                notificationAppServiceConnection.RequestReceived -= OnNotificationAppServiceRequestReceived;
                notificationAppServiceConnection.RequestReceived += OnNotificationAppServiceRequestReceived;
                notificationAppServiceConnection.ServiceClosed -= NotificationAppServiceConnection_ServiceClosed;
                notificationAppServiceConnection.ServiceClosed += NotificationAppServiceConnection_ServiceClosed;
            }
            else if (appService?.Name == "com.roamit.messagecarrierservice")
            {
                messageCarrierAppServiceDeferral = taskInstance.GetDeferral();
                taskInstance.Canceled -= OnMessageCarrierAppServicesCanceled;
                taskInstance.Canceled += OnMessageCarrierAppServicesCanceled;
                messageCarrierAppServiceConnection = appService.AppServiceConnection;
                messageCarrierAppServiceConnection.RequestReceived -= OnMessageCarrierAppServiceRequestReceived;
                messageCarrierAppServiceConnection.RequestReceived += OnMessageCarrierAppServiceRequestReceived;
                messageCarrierAppServiceConnection.ServiceClosed -= MessageCarrierAppServiceConnection_ServiceClosed;
                messageCarrierAppServiceConnection.ServiceClosed += MessageCarrierAppServiceConnection_ServiceClosed;
            }
            else if (appService?.Name == "com.roamit.pcservice")
            {
                pcAppServiceDeferral = taskInstance.GetDeferral();
                taskInstance.Canceled -= OnPCAppServicesCanceled;
                taskInstance.Canceled += OnPCAppServicesCanceled;
                pcAppServiceConnection = appService.AppServiceConnection;
                pcAppServiceConnection.RequestReceived -= OnPCAppServiceRequestReceived;
                pcAppServiceConnection.RequestReceived += OnPCAppServiceRequestReceived;
                pcAppServiceConnection.ServiceClosed -= PCAppServiceConnection_ServiceClosed;
                pcAppServiceConnection.ServiceClosed += PCAppServiceConnection_ServiceClosed;
            }
            else if (appService?.Name == "com.roamit.serviceinapp")
            {
                InitCommunicationService();

                communicationServiceDeferral = taskInstance.GetDeferral();
                taskInstance.Canceled -= OnCommunicationServicesCanceled;
                taskInstance.Canceled += OnCommunicationServicesCanceled;
                communicationServiceConnection = appService.AppServiceConnection;
                communicationServiceConnection.RequestReceived -= OnCommunicationServiceRequestReceived;
                communicationServiceConnection.RequestReceived += OnCommunicationServiceRequestReceived;
                communicationServiceConnection.ServiceClosed -= CommunicationServiceConnection_ServiceClosed;
                communicationServiceConnection.ServiceClosed += CommunicationServiceConnection_ServiceClosed;
            }
        }

        public static ShareOperation ShareOperation;
        protected override async void OnShareTargetActivated(ShareTargetActivatedEventArgs args)
        {
            ShareOperation = args.ShareOperation;
            string type = await ExternalContentHelper.SetData(ShareOperation.Data);

            if (type == "")
            {
                ShareOperation.ReportError("Unknown data type received.");
                return;
            }

            ShareOperation.ReportDataRetrieved();
            SendDataTemporaryStorage.IsSharingTarget = true;

            Frame rootFrame = null;
            LaunchRootFrameIfNecessary(ref rootFrame, false);
            rootFrame.Navigate(typeof(MainPage), new ShareTargetDetails
            {
                Type = type,
            });
        }
    }
}
