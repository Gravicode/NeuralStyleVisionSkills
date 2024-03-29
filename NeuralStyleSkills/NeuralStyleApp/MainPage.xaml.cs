﻿using Microsoft.AI.Skills.SkillInterfacePreview;
using NeuralStyleTransformer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace NeuralStyleApp
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        // Camera related
        private MediaCapture _mediaCapture;
        private MediaPlayer _mediaPlayer;
        private List<MediaFrameSourceGroup> _mediaFrameSourceGroupList;
        private MediaFrameSourceGroup _selectedMediaFrameSourceGroup;
        private MediaFrameSource _selectedMediaFrameSource;
        private MediaFrameReader _modelInputFrameReader;

        // States
        private bool _isReadyForEval = true;
        private bool _isProcessingFrames = false;
        private bool _showInitialImageAndProgress = true;
        private SemaphoreSlim _evaluationLock = new SemaphoreSlim(1);
        private SemaphoreSlim _frameAquisitionLock = new SemaphoreSlim(1);
       
        private DispatcherTimer inkEvaluationDispatcherTimer;
        private bool _isrocessingImages = true;
        private bool _proceedWithEval = true;

        // Rendering related
        private FrameRenderer _resultframeRenderer;
        private FrameRenderer _inputFrameRenderer;

        // WinML related
        private readonly List<string> _kModelFileNames = new List<string>
        {
           "Candy","Mosaic","Pointilism", "RainPrincess", "Udnie"
        };
        private const string _kDefaultImageFileName = "DefaultImage.jpg";
       
       
        private List<string> _labels = new List<string>();
        VideoFrame _inputFrame = null;
        VideoFrame _outputFrame = null;

        // Debug
        private Stopwatch _perfStopwatch = new Stopwatch(); // performance Stopwatch used throughout
        private DispatcherTimer _FramesPerSecondTimer = new DispatcherTimer();
        private long _CaptureFPS = 0;
        public static long _RenderFPS = 0;
        private int _LastFPSTick = 0;

        // Skill-related variables
        private NeuralStyleTransformerDescriptor m_skillDescriptor = null;
        private NeuralStyleTransformerSkill m_skill = null;
        private NeuralStyleTransformerBinding m_binding = null;

        // UI-related variables
        private SoftwareBitmapSource m_bitmapSource = new SoftwareBitmapSource(); // used to render an image from a file
        
        private IReadOnlyList<ISkillExecutionDevice> m_availableExecutionDevices = null;

        // Synchronization
        private SemaphoreSlim m_lock = new SemaphoreSlim(1);

        /// <summary>
        /// MainPage constructor
        /// </summary>
        public MainPage()
        {
            this.InitializeComponent();
        }

        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            Debug.WriteLine("OnNavigatedTo");
            try
            {
                // Instatiate skill descriptor to display details about the skill and populate UI
                m_skillDescriptor = new NeuralStyleTransformerDescriptor();
                m_availableExecutionDevices = await m_skillDescriptor.GetSupportedExecutionDevicesAsync();

                // Show skill description members in UI
                UISkillName.Text = m_skillDescriptor.Name;

                UISkillDescription.Text = SkillHelper.SkillHelperMethods.GetSkillDescriptorString(m_skillDescriptor);

                int featureIndex = 0;
                foreach (var featureDesc in m_skillDescriptor.InputFeatureDescriptors)
                {
                    UISkillInputDescription.Text += SkillHelper.SkillHelperMethods.GetSkillFeatureDescriptorString(featureDesc);
                    if (featureIndex++ < m_skillDescriptor.InputFeatureDescriptors.Count - 1)
                    {
                        UISkillInputDescription.Text += "\n----\n";
                    }
                }

                featureIndex = 0;
                foreach (var featureDesc in m_skillDescriptor.OutputFeatureDescriptors)
                {
                    UISkillOutputDescription.Text += SkillHelper.SkillHelperMethods.GetSkillFeatureDescriptorString(featureDesc);
                    if (featureIndex++ < m_skillDescriptor.OutputFeatureDescriptors.Count - 1)
                    {
                        UISkillOutputDescription.Text += "\n----\n";
                    }
                }

                if (m_availableExecutionDevices.Count == 0)
                {
                    UISkillOutputDetails.Text = "No execution devices available, this skill cannot run on this device";
                }
                else
                {
                    // Display available execution devices
                    UISkillExecutionDevices.ItemsSource = m_availableExecutionDevices.Select((device) => device.Name).ToList();
                    UISkillExecutionDevices.SelectedIndex = 0;

                    // Alow user to interact with the app
                    UIButtonFilePick.IsEnabled = true;
                   
                    UIButtonFilePick.Focus(FocusState.Keyboard);
                }
            }
            catch (Exception ex)
            {
                await new MessageDialog(ex.Message).ShowAsync();
            }
            
            _resultframeRenderer = new FrameRenderer(UIResultImage);
            _inputFrameRenderer = new FrameRenderer(UIInputImage);
            UIStyleList.ItemsSource = _kModelFileNames;

            UIInkCanvasInput.InkPresenter.InputDeviceTypes =
               CoreInputDeviceTypes.Mouse
               | CoreInputDeviceTypes.Pen
               | CoreInputDeviceTypes.Touch;

            UIInkCanvasInput.InkPresenter.UpdateDefaultDrawingAttributes(
                new Windows.UI.Input.Inking.InkDrawingAttributes()
                {
                    Color = Windows.UI.Colors.Black,
                    Size = new Size(8, 8),
                    IgnorePressure = true,
                    IgnoreTilt = true,
                }
            );

            // Select first style
            UIStyleList.SelectedIndex = 0;

            // Create a 1 second timer
            _FramesPerSecondTimer.Tick += _FramesPerSecond_Tick;
            _FramesPerSecondTimer.Interval = new TimeSpan(0, 0, 1);
            _FramesPerSecondTimer.Start();

          
        }

        private void _FramesPerSecond_Tick(object sender, object e)
        {
            // how many seconds has it been?
            // Note: we do this math since even though we asked for the event to be 
            // dispatched every 1s , due to timing and delays, it might not come
            // exactly every second.   and on a busy system it could even be a couple of
            // seconds until it is delivered.
            int fpsTick = System.Environment.TickCount;

            if (_LastFPSTick > 0)
            {
                float numberOfSeconds = ((float)(fpsTick - _LastFPSTick)) / (float)1000;

                // how many frames did we capture?
                float intervalFPS = ((float)_CaptureFPS) / numberOfSeconds;
                if (intervalFPS == 0.0)
                    return;
                NotifyUser(CaptureFPS, $"{intervalFPS:F1}", NotifyType.StatusMessage);

                // how many frames did we render
                intervalFPS = ((float)_RenderFPS) / numberOfSeconds;
                if (intervalFPS == 0.0)
                    return;
                NotifyUser(RenderFPS, $"{intervalFPS:F1}", NotifyType.StatusMessage);
            }

            _CaptureFPS = 0;
            _RenderFPS = 0;
            _LastFPSTick = fpsTick;
        }

        public void NotifyUser(string strMessage, NotifyType type)
        {
            NotifyUser(StatusBlock, strMessage, type);
        }

        /// <summary>
        /// Display a message to the user.
        /// This method may be called from any thread.
        /// </summary>
        /// <param name="strMessage"></param>
        /// <param name="type"></param>
        public void NotifyUser(TextBlock block, string strMessage, NotifyType type)
        {
            // If called from the UI thread, then update immediately.
            // Otherwise, schedule a task on the UI thread to perform the update.
            if (Dispatcher.HasThreadAccess)
            {
                UpdateStatus(block, strMessage, type);
            }
            else
            {
                var task = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => UpdateStatus(block, strMessage, type));
                task.AsTask().Wait();
            }
        }

        /// <summary>
        /// Update the status message displayed on the UI
        /// </summary>
        /// <param name="strMessage"></param>
        /// <param name="type"></param>
        private void UpdateStatus(TextBlock block, string strMessage, NotifyType type)
        {
            switch (type)
            {
                case NotifyType.StatusMessage:
                    UIStatusBorder.Background = new SolidColorBrush(Windows.UI.Colors.Green);
                    break;
                case NotifyType.ErrorMessage:
                    UIStatusBorder.Background = new SolidColorBrush(Windows.UI.Colors.Red);
                    break;
            }

            block.Text = strMessage;

            // Collapse the TextBlock if it has no text to conserve real estate.
            UIStatusBorder.Visibility = (block.Text != String.Empty) ? Visibility.Visible : Visibility.Collapsed;
            if (block.Text != String.Empty)
            {
                UIStatusBorder.Visibility = Visibility.Visible;
                UIStatusPanel.Visibility = Visibility.Visible;
            }
            else
            {
                UIStatusBorder.Visibility = Visibility.Collapsed;
                UIStatusPanel.Visibility = Visibility.Collapsed;
            }
        }


        /// <summary>
        /// Acquire manually an image from the camera preview stream
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void UIButtonAcquireImage_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("UIButtonAcquireImage_Click");
            _evaluationLock.Wait();
            {
                _proceedWithEval = false;
                await CleanupCameraAsync();
                CleanupInk();
            }
            _evaluationLock.Release();

            UIInputImage.Visibility = Visibility.Visible;
            _showInitialImageAndProgress = true;
            UIImageControls.IsEnabled = false;
            UIModelControls.IsEnabled = false;

            CameraCaptureUI dialog = new CameraCaptureUI();
            dialog.PhotoSettings.AllowCropping = false;
            dialog.PhotoSettings.Format = CameraCaptureUIPhotoFormat.Png;

            StorageFile file = await dialog.CaptureFileAsync(CameraCaptureUIMode.Photo);
            if (file != null)
            {
                var vf = await ImageHelper.LoadVideoFrameFromStorageFileAsync(file);
                await Task.Run(() =>
                {
                    _proceedWithEval = true;
                    EvaluateVideoFrameAsync(vf).ConfigureAwait(false).GetAwaiter().GetResult();
                });
            }

            UIImageControls.IsEnabled = true;
            UIModelControls.IsEnabled = true;
        }

        /// <summary>
        /// Select and evaluate a picture
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void UIButtonFilePick_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("UIButtonFilePick_Click");
            _evaluationLock.Wait();
            {
                _proceedWithEval = false;
                await CleanupCameraAsync();
                CleanupInk();
            }
            _evaluationLock.Release();

            UIInputImage.Visibility = Visibility.Visible;
            _showInitialImageAndProgress = true;
            _isrocessingImages = true;
            UIImageControls.IsEnabled = false;
            UIModelControls.IsEnabled = false;
            try
            {
                VideoFrame inputFrame = null;
                // use a default image
                if (sender == null && e == null)
                {
                    if (_inputFrame == null)
                    {
                        var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri($"ms-appx:///Assets/{_kDefaultImageFileName}"));
                        inputFrame = await ImageHelper.LoadVideoFrameFromStorageFileAsync(file);
                    }
                    else
                    {
                        inputFrame = _inputFrame;
                    }
                }
                else
                {
                    // Load image to VideoFrame
                    inputFrame = await ImageHelper.LoadVideoFrameFromFilePickedAsync();
                }
                if (inputFrame == null)
                {
                    NotifyUser("no valid image file selected", NotifyType.ErrorMessage);
                }
                else
                {
                    await Task.Run(() =>
                    {
                        _proceedWithEval = true;
                        EvaluateVideoFrameAsync(inputFrame).ConfigureAwait(false).GetAwaiter().GetResult();
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"error: {ex.Message}");
                NotifyUser(ex.Message, NotifyType.ErrorMessage);
            }
            UIImageControls.IsEnabled = true;
            UIModelControls.IsEnabled = true;
        }

        /// <summary>
        /// 1) Bind input and output features 
        /// 2) Run evaluation of the model
        /// 3) Report the result
        /// </summary>
        /// <param name="inputVideoFrame"></param>
        /// <returns></returns>
        private async Task EvaluateVideoFrameAsync(VideoFrame inputVideoFrame)
        {
            Debug.WriteLine("EvaluateVideoFrameAsync");
            
            bool isReadyForEval = false;
            bool showInitialImageAndProgress = true;
            bool proceedWithEval = false;
            _evaluationLock.Wait();
            {
                
                isReadyForEval = _isReadyForEval;
                _isReadyForEval = false;
                showInitialImageAndProgress = _showInitialImageAndProgress;
                proceedWithEval = _proceedWithEval;
            }
            _evaluationLock.Release();

            if ((inputVideoFrame != null) &&
                (inputVideoFrame.SoftwareBitmap != null || inputVideoFrame.Direct3DSurface != null) &&
                isReadyForEval &&

                proceedWithEval)
            {
                try
                {
                    _perfStopwatch.Restart();
                    NotifyUser("Processing...", NotifyType.StatusMessage);
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async() =>
                    {
                        if (showInitialImageAndProgress)
                        {
                            UIProcessingProgressRing.IsActive = true;
                            UIProcessingProgressRing.Visibility = Visibility.Visible;
                            UIButtonSaveImage.IsEnabled = false;
                        }


                        // Crop the input image to communicate appropriately to the user what is being evaluated
                        _inputFrame = await ImageHelper.CenterCropImageAsync(inputVideoFrame, NeuralStyleTransformerConst.IMAGE_WIDTH, NeuralStyleTransformerConst.IMAGE_HEIGHT);

                        _perfStopwatch.Stop();
                        Int64 cropTime = _perfStopwatch.ElapsedMilliseconds;
                        Debug.WriteLine($"Image handling: {cropTime}ms");

                        // Bind and Eval
                        if (_inputFrame != null)
                        {
                            _evaluationLock.Wait();
                            try
                            {
                                _perfStopwatch.Restart();
                                await m_binding.SetInputImageAsync(_inputFrame);

                                Int64 bindTime = _perfStopwatch.ElapsedMilliseconds;
                                Debug.WriteLine($"Binding: {bindTime}ms");
                                // render the input frame 
                                if (showInitialImageAndProgress)
                                {
                                    await ImageHelper.RenderFrameAsync(_inputFrameRenderer, _inputFrame);
                                }

                                // Process the frame with the model
                                _perfStopwatch.Restart();
                                await m_skill.EvaluateAsync(m_binding);


                                _perfStopwatch.Stop();
                                Int64 evalTime = _perfStopwatch.ElapsedMilliseconds;
                                Debug.WriteLine($"Eval: {evalTime}ms");

                                _outputFrame = (m_binding["OutputImage"].FeatureValue as SkillFeatureImageValue).VideoFrame;
                                UISkillOutputDetails.Text = "";

                                await ImageHelper.RenderFrameAsync(_resultframeRenderer, _outputFrame);


                            }
                            catch (Exception ex)
                            {
                                NotifyUser(ex.Message, NotifyType.ErrorMessage);
                                Debug.WriteLine(ex.ToString());
                            }
                            finally
                            {
                                _evaluationLock.Release();
                            }

                            if (showInitialImageAndProgress)
                            {

                                UIProcessingProgressRing.IsActive = false;
                                UIProcessingProgressRing.Visibility = Visibility.Collapsed;
                                UIButtonSaveImage.IsEnabled = true;

                            }

                            NotifyUser("Done!", NotifyType.StatusMessage);
                        }
                        else
                        {
                            Debug.WriteLine("Skipped eval, null input frame");
                        }
                    });
                }
                catch (Exception ex)
                {
                    NotifyUser(ex.Message, NotifyType.ErrorMessage);
                    Debug.WriteLine(ex.ToString());
                }

                _evaluationLock.Wait();
                {
                    _isReadyForEval = true;
                }
                _evaluationLock.Release();

                _perfStopwatch.Reset();
            }
        }

        /// <summary>
        /// Change style to apply to the input frame
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void UIStyleList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Debug.WriteLine("UIStyleList_SelectionChanged");
            if (UIStyleList.SelectedIndex < 0)
            {
                return;
            }
            var selection = UIStyleList.SelectedIndex;
            UIModelControls.IsEnabled = false;
            UIImageControls.IsEnabled = false;



            m_skillDescriptor.SetStyle((StyleChoices)selection);
            // Initialize skill with the selected supported device
            var sel = m_availableExecutionDevices[UISkillExecutionDevices.SelectedIndex];
            m_skill = await m_skillDescriptor.CreateSkillAsync(sel) as NeuralStyleTransformerSkill;

            // Instantiate a binding object that will hold the skill's input and output resource
            m_binding = await m_skill.CreateSkillBindingAsync() as NeuralStyleTransformerBinding;

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                NotifyUser($"Ready to stylize! ", NotifyType.StatusMessage);
                UIImageControls.IsEnabled = true;
                UIModelControls.IsEnabled = true;

                if (_isrocessingImages)
                {
                    UIButtonFilePick_Click(null, null);
                }
            });


        }

        /// <summary>
        /// Save image result to file
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void UIButtonSaveImage_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("UIButtonSaveImage_Click");
            await ImageHelper.SaveVideoFrameToFilePickedAsync(_outputFrame);
        }

        /// <summary>
        /// Apply effect on ink handdrawn
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void UIButtonInking_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("UIButtonInking_Click");
            _evaluationLock.Wait();
            {
                _proceedWithEval = false;
                await CleanupCameraAsync();
                CleanupInk();
                CleanupInputImage();
            }
            _evaluationLock.Release();

            UIInkControls.Visibility = Visibility.Visible;
            UIResultImage.Width = UIInkControls.Width;
            UIResultImage.Height = UIInkControls.Height;
            _showInitialImageAndProgress = false;

            UIImageControls.IsEnabled = false;
            UIModelControls.IsEnabled = false;

            inkEvaluationDispatcherTimer = new DispatcherTimer();
            inkEvaluationDispatcherTimer.Tick += async (object a, object b) =>
            {
                if (!_frameAquisitionLock.Wait(100))
                {
                    return;
                }
                {
                    if (_isProcessingFrames)
                    {
                        _frameAquisitionLock.Release();
                        return;
                    }

                    _isProcessingFrames = true;
                }
                _frameAquisitionLock.Release();

                try
                {
                    if (UIInkControls.Visibility != Visibility.Visible)
                    {
                        throw (new Exception("invisible control, will not attempt rendering"));
                    }
                    // Render the ink control to an image
                    RenderTargetBitmap renderBitmap = new RenderTargetBitmap();
                    await renderBitmap.RenderAsync(UIInkGrid);
                    var buffer = await renderBitmap.GetPixelsAsync();
                    var softwareBitmap = SoftwareBitmap.CreateCopyFromBuffer(buffer, BitmapPixelFormat.Bgra8, renderBitmap.PixelWidth, renderBitmap.PixelHeight, BitmapAlphaMode.Ignore);
                    buffer = null;
                    renderBitmap = null;

                    // Instantiate VideoFrame using the softwareBitmap of the ink
                    VideoFrame vf = VideoFrame.CreateWithSoftwareBitmap(softwareBitmap);

                    // Evaluate the VideoFrame
                    await Task.Run(() =>
                    {
                        EvaluateVideoFrameAsync(vf).ConfigureAwait(false).GetAwaiter().GetResult();
                        _frameAquisitionLock.Wait();
                        {
                            _isProcessingFrames = false;
                        }
                        _frameAquisitionLock.Release();
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
            };

            inkEvaluationDispatcherTimer.Interval = new TimeSpan(0, 0, 0, 0, 33);
            inkEvaluationDispatcherTimer.Start();

            _evaluationLock.Wait();
            {
                _proceedWithEval = true;
            }
            _evaluationLock.Release();

            UIImageControls.IsEnabled = true;
            UIModelControls.IsEnabled = true;
        }

        /// <summary>
        /// Cleanup inking resources
        /// </summary>
        private void CleanupInk()
        {
            Debug.WriteLine("CleanupInk");
            _frameAquisitionLock.Wait();
            try
            {
                if (inkEvaluationDispatcherTimer != null)
                {
                    inkEvaluationDispatcherTimer.Stop();
                    inkEvaluationDispatcherTimer = null;
                }
                UIInkCanvasInput.InkPresenter.StrokeContainer.Clear();
                UIInkControls.Visibility = Visibility.Collapsed;
                _showInitialImageAndProgress = true;
                _isProcessingFrames = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CleanupInk: {ex.Message}");
            }
            finally
            {
                _frameAquisitionLock.Release();
            }
        }

        /// <summary>
        /// Cleanup input image resources
        /// </summary>
        private void CleanupInputImage()
        {
            Debug.WriteLine("CleanupInputImage");
            _frameAquisitionLock.Wait();
            try
            {
                UIInputImage.Visibility = Visibility.Collapsed;
                _isrocessingImages = false;
            }
            finally
            {
                _frameAquisitionLock.Release();
            }
        }

        /// <summary>
        /// Apply effect in real time to a camera feed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void UIButtonLiveStream_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("UIButtonLiveStream_Click");
            _evaluationLock.Wait();
            {
                _proceedWithEval = false;
                await CleanupCameraAsync();
                CleanupInk();
                CleanupInputImage();
            }
            _evaluationLock.Release();

            await InitializeMediaCaptureAsync();
        }

        /// <summary>
        /// Initialize MediaCapture for live stream scenario
        /// </summary>
        /// <returns></returns>
        private async Task InitializeMediaCaptureAsync()
        {
            Debug.WriteLine("InitializeMediaCaptureAsync");
            _frameAquisitionLock.Wait();
            try
            {
                // Find the sources 
                var allGroups = await MediaFrameSourceGroup.FindAllAsync();

                _mediaFrameSourceGroupList = allGroups.Where(group => group.SourceInfos.Any(sourceInfo => sourceInfo.SourceKind == MediaFrameSourceKind.Color
                                                                                                           && (sourceInfo.MediaStreamType == MediaStreamType.VideoPreview
                                                                                                               || sourceInfo.MediaStreamType == MediaStreamType.VideoRecord))).ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                NotifyUser(ex.Message, NotifyType.ErrorMessage);
                _mediaFrameSourceGroupList = null;
            }
            finally
            {
                _frameAquisitionLock.Release();
            }

            if ((_mediaFrameSourceGroupList == null) || (_mediaFrameSourceGroupList.Count == 0))
            {
                // No camera sources found
                Debug.WriteLine("No Camera found");
                NotifyUser("No Camera found", NotifyType.ErrorMessage);
                return;
            }

            var cameraNamesList = _mediaFrameSourceGroupList.Select(group => group.DisplayName);
            UICmbCamera.ItemsSource = cameraNamesList;
            UICmbCamera.SelectedIndex = 0;
        }

       

        /// <summary>
        /// Start previewing from the camera
        /// </summary>
        private void StartPreview()
        {
            Debug.WriteLine("StartPreview");
            _selectedMediaFrameSource = _mediaCapture.FrameSources.FirstOrDefault(source => source.Value.Info.MediaStreamType == MediaStreamType.VideoPreview
                                                                                  && source.Value.Info.SourceKind == MediaFrameSourceKind.Color).Value;
            if (_selectedMediaFrameSource == null)
            {
                _selectedMediaFrameSource = _mediaCapture.FrameSources.FirstOrDefault(source => source.Value.Info.MediaStreamType == MediaStreamType.VideoRecord
                                                                                      && source.Value.Info.SourceKind == MediaFrameSourceKind.Color).Value;
            }

            // if no preview stream are available, bail
            if (_selectedMediaFrameSource == null)
            {
                return;
            }

            _mediaPlayer = new MediaPlayer();
            _mediaPlayer.RealTimePlayback = true;
            _mediaPlayer.AutoPlay = true;
            _mediaPlayer.Source = MediaSource.CreateFromMediaFrameSource(_selectedMediaFrameSource);
            UIMediaPlayerElement.SetMediaPlayer(_mediaPlayer);
            UITxtBlockPreviewProperties.Text = string.Format("{0}x{1}@{2}, {3}",
                        _selectedMediaFrameSource.CurrentFormat.VideoFormat.Width,
                        _selectedMediaFrameSource.CurrentFormat.VideoFormat.Height,
                        _selectedMediaFrameSource.CurrentFormat.FrameRate.Numerator + "/" + _selectedMediaFrameSource.CurrentFormat.FrameRate.Denominator,
                        _selectedMediaFrameSource.CurrentFormat.Subtype);

            UICameraSelectionControls.Visibility = Visibility.Visible;
            UIMediaPlayerElement.Visibility = Visibility.Visible;
            UIResultImage.Width = UIMediaPlayerElement.Width;
            UIResultImage.Height = UIMediaPlayerElement.Height;
        }

        /// <summary>
        /// A new frame from the camera is available
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void _modelInputFrameReader_FrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
        {
            Debug.WriteLine("_modelInputFrameReader_FrameArrived");
            MediaFrameReference frame = null;
            if (_isProcessingFrames)
            {
                return;
            }
            // Do not attempt processing of more than 1 frame at a time
            _frameAquisitionLock.Wait();
            {
                _isProcessingFrames = true;
                _CaptureFPS += 1;

                try
                {
                    frame = sender.TryAcquireLatestFrame();
                   
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                    NotifyUser(ex.Message, NotifyType.ErrorMessage);
                    frame = null;
                }

                if ((frame != null) && (frame.VideoMediaFrame != null))
                {
                    VideoFrame vf = null;

                    // Receive frames from the camera and transfer to system memory 
                    _perfStopwatch.Restart();
                    SoftwareBitmap softwareBitmap = frame.VideoMediaFrame.SoftwareBitmap;

                    if (softwareBitmap == null) // frames are coming as Direct3DSurface
                    {
                        Debug.Assert(frame.VideoMediaFrame.Direct3DSurface != null);
                        vf = VideoFrame.CreateWithDirect3D11Surface(frame.VideoMediaFrame.Direct3DSurface);
                    }
                    else
                    {
                        vf = VideoFrame.CreateWithSoftwareBitmap(softwareBitmap);
                    }
                    EvaluateVideoFrameAsync(vf).ConfigureAwait(false).GetAwaiter().GetResult();
                }
                Thread.Sleep(500);
                _isProcessingFrames = false;
            }
            _frameAquisitionLock.Release();
        }

        /// <summary>
        /// On selected camera changed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void UICmbCamera_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Debug.WriteLine("UICmbCamera_SelectionChanged");
            if ((_mediaFrameSourceGroupList.Count == 0) || (UICmbCamera.SelectedIndex < 0))
            {
                return;
            }

            _evaluationLock.Wait();
            {
                _proceedWithEval = false;
                await CleanupCameraAsync();
                CleanupInk();
                CleanupInputImage();
            }
            _evaluationLock.Release();

            UIImageControls.IsEnabled = false;
            UIModelControls.IsEnabled = false;

            _frameAquisitionLock.Wait();
            try
            {
                _selectedMediaFrameSourceGroup = _mediaFrameSourceGroupList[UICmbCamera.SelectedIndex];

                // Create MediaCapture and its settings
                _mediaCapture = new MediaCapture();
                var settings = new MediaCaptureInitializationSettings
                {
                    SourceGroup = _selectedMediaFrameSourceGroup,
                    PhotoCaptureSource = PhotoCaptureSource.Auto,
                    MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                    StreamingCaptureMode = StreamingCaptureMode.Video
                };

                // Initialize MediaCapture
                await _mediaCapture.InitializeAsync(settings);
                StartPreview();

                if (m_skillDescriptor != null)
                {
                    await InitializeModelInputFrameReaderAsync();
                }

                _evaluationLock.Wait();
                {
                    _proceedWithEval = true;
                }
                _evaluationLock.Release();
            }
            catch (Exception ex)
            {
                NotifyUser(ex.Message, NotifyType.ErrorMessage);
                Debug.WriteLine(ex.ToString());
            }
            finally
            {
                _frameAquisitionLock.Release();
            }

            UIImageControls.IsEnabled = true;
            UIModelControls.IsEnabled = true;
        }

        /// <summary>
        /// Initialize the camera frame reader and preview UI element
        /// </summary>
        /// <returns></returns>
        private async Task InitializeModelInputFrameReaderAsync()
        {
            Debug.WriteLine("InitializeModelInputFrameReaderAsync");
            // Create the MediaFrameReader
            try
            {
                if (_modelInputFrameReader != null)
                {
                    await _modelInputFrameReader.StopAsync();
                    _modelInputFrameReader.FrameArrived -= _modelInputFrameReader_FrameArrived;
                }

                string frameReaderSubtype = _selectedMediaFrameSource.CurrentFormat.Subtype;
                if (string.Compare(frameReaderSubtype, MediaEncodingSubtypes.Nv12, true) != 0 &&
                    string.Compare(frameReaderSubtype, MediaEncodingSubtypes.Bgra8, true) != 0 &&
                    string.Compare(frameReaderSubtype, MediaEncodingSubtypes.Yuy2, true) != 0 &&
                    string.Compare(frameReaderSubtype, MediaEncodingSubtypes.Rgb32, true) != 0)
                {
                    frameReaderSubtype = MediaEncodingSubtypes.Bgra8;
                }

                _modelInputFrameReader = null;
                _modelInputFrameReader = await _mediaCapture.CreateFrameReaderAsync(_selectedMediaFrameSource, frameReaderSubtype);
                _modelInputFrameReader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
                _isProcessingFrames = false;

                await _modelInputFrameReader.StartAsync();

                _modelInputFrameReader.FrameArrived += _modelInputFrameReader_FrameArrived;
                _showInitialImageAndProgress = false;
            }
            catch (Exception ex)
            {
                NotifyUser($"Error while initializing MediaframeReader: " + ex.Message, NotifyType.ErrorMessage);
                Debug.WriteLine(ex.ToString());
            }
        }

        /// <summary>
        /// Cleanup camera used for live stream scenario
        /// </summary>
        private async Task CleanupCameraAsync()
        {
            Debug.WriteLine("CleanupCameraAsync");
            _frameAquisitionLock.Wait();
            try
            {
                _showInitialImageAndProgress = true;
                _isProcessingFrames = false;
                if (_modelInputFrameReader != null)
                {
                    _modelInputFrameReader.FrameArrived -= _modelInputFrameReader_FrameArrived;
                }
                _modelInputFrameReader = null;

                if (_mediaCapture != null)
                {
                    _mediaCapture = null;
                }
                if (_mediaPlayer != null)
                {
                    _mediaPlayer.Source = null;
                    _mediaPlayer = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CleanupCameraAsync: {ex.Message}");
            }
            finally
            {
                _frameAquisitionLock.Release();
            }

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                UICameraSelectionControls.Visibility = Visibility.Collapsed;
                UIMediaPlayerElement.Visibility = Visibility.Collapsed;
            });
        }

        private void UISkillExecutionDevices_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Debug.WriteLine("UIToggleInferenceDevice_Toggled");
            if (UIStyleList == null)
            {
                return;
            }
            UIStyleList_SelectionChanged(null, null);
        }
    }
    public enum NotifyType
    {
        StatusMessage,
        ErrorMessage
    };
}
