using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace XMIT501_CS
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        // Core Objects
        private AudioEngine? _audioEngine;
        private RadioClient? _client;
        private RadioServer? _server;
        private MediaPlayer _sfxPlayer = new MediaPlayer();
        private MediaPlayer _staticPlayer = new MediaPlayer();

        // Routing & State
        public HashSet<byte> ActiveRxChannels { get; } = new HashSet<byte>();
        public HashSet<byte> ActiveTxChannels { get; } = new HashSet<byte>();
        private bool _isRadioOn = false;
        private bool _isTransmitting = false;
        private DispatcherTimer _rxLedTimer;
        private DispatcherTimer _connLedTimer;

        // Manage SFX
        private bool _playBlip = true;
        private bool _playStatic = true;
        private string SfxPowerOn = "";
        private string SfxPowerOff = "";
        private string SfxSwitchOn = "";
        private string SfxSwitchOff = "";
        private string SfxBlip = "";
        private string SfxStatic = "";

        // Settings State
        private int _bindingState = 0; // 0=None, 1=Global, 2=Ch1, 3=Ch2, 4=Ch3
        public byte? ActiveOverrideChannel = null; // Tells the network client if we are overriding

        private List<int> _pttVirtualKeys = new List<int> { 0x56 }; 
        private List<int> _pttCh1Keys = new List<int>(); 
        private List<int> _pttCh2Keys = new List<int>(); 
        private List<int> _pttCh3Keys = new List<int>();

        private InputManager _inputManager = new InputManager();

        // Add these to store current joystick bindings in memory
        private string _pttJoyGuid = "", _pttCh1JoyGuid = "", _pttCh2JoyGuid = "", _pttCh3JoyGuid = "";
        private int _pttJoyButton = -1, _pttCh1JoyButton = -1, _pttCh2JoyButton = -1, _pttCh3JoyButton = -1;
        private bool _isBindingPtt = false;
        private string _currentIp = "127.0.0.1";
        private int _currentPort = 5000;
        private DispatcherTimer _pttTimer;
        private readonly string _configPath = "config.json";

        public MainWindow()
        {
            InitializeComponent();
            LoadConfig();

            // 1. Setup Temp Directory
            string tempDir = Path.Combine(Path.GetTempPath(), "XMIT501_SFX");
            Directory.CreateDirectory(tempDir);

            // 2. Extract and Map Sounds
            string[] sounds = { "turnswitch_on.wav", "turnswitch_off.wav", "flipswitch_on.wav", "flipswitch_off.wav", "blip.wav", "static.wav" };
            foreach (var sfx in sounds)
            {
                string outPath = Path.Combine(tempDir, sfx);
                var resUri = new Uri($"pack://application:,,,/assets/sfx/{sfx}");
                var resource = Application.GetResourceStream(resUri);

                if (resource != null)
                {
                    try
                    {
                        // We use a FileStream and copy only once
                        using (var fs = File.Create(outPath))
                        {
                            resource.Stream.CopyTo(fs);
                        }
                    }
                    catch { /* File might be locked by another instance, that's fine */ }

                    // Map the extracted path to your variables
                    if (sfx == "turnswitch_on.wav") SfxPowerOn = outPath;
                    else if (sfx == "turnswitch_off.wav") SfxPowerOff = outPath;
                    else if (sfx == "flipswitch_on.wav") SfxSwitchOn = outPath;
                    else if (sfx == "flipswitch_off.wav") SfxSwitchOff = outPath;
                    else if (sfx == "blip.wav") SfxBlip = outPath;
                    else if (sfx == "static.wav") SfxStatic = outPath;
                }
                else
                {
                    MessageBox.Show($"MISSING ASSET: {sfx} not found in resources.");
                }
            }

            // 3. Initialize Players
            if (!string.IsNullOrEmpty(SfxStatic))
            {
                _staticPlayer.Open(new Uri(SfxStatic));
                _staticPlayer.Volume = 0.2;
                _staticPlayer.MediaEnded += (s, e) => {
                    _staticPlayer.Position = TimeSpan.Zero;
                    _staticPlayer.Play();
                };
            }

            // 4. Timers
            _pttTimer = new DispatcherTimer();
            _pttTimer.Interval = TimeSpan.FromMilliseconds(30);
            _pttTimer.Tick += CheckPttKey;
            _pttTimer.Start();

            _rxLedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _rxLedTimer.Tick += (s, e) =>
            {
                LedRx.Visibility = Visibility.Hidden;
                LedRx.Opacity = 1.0;
                _rxLedTimer.Stop();
            };

            _connLedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _connLedTimer.Tick += (s, e) =>
            {
                LedConn.Source = new BitmapImage(new Uri("assets/buttons/light_red.png", UriKind.Relative));
                _connLedTimer.Stop();
                StationLine1.Text = "OFFLINE";
                StationLine2.Text = "NO CARRIER";
            };
        }

        private void LoadConfig()
        {
            if (File.Exists(_configPath))
            {
                try 
                {
                    string json = File.ReadAllText(_configPath);
                    var config = JsonSerializer.Deserialize<AppConfig>(json);
                    
                    if (config != null) 
                    {
                        IpTextBox.Text = config.TargetIp;
                        HostServerCheckbox.IsChecked = config.HostServer;
                        
                        // 1. Load all variables into memory
                        if (config.PttKeys != null) _pttVirtualKeys = config.PttKeys;
                        if (config.PttCh1Keys != null) _pttCh1Keys = config.PttCh1Keys;
                        if (config.PttCh2Keys != null) _pttCh2Keys = config.PttCh2Keys;
                        if (config.PttCh3Keys != null) _pttCh3Keys = config.PttCh3Keys;

                        _pttJoyGuid = config.PttJoyGuid ?? "";
                        _pttJoyButton = config.PttJoyButton;
                        _pttCh1JoyGuid = config.PttCh1JoyGuid ?? "";
                        _pttCh1JoyButton = config.PttCh1JoyButton;
                        _pttCh2JoyGuid = config.PttCh2JoyGuid ?? "";
                        _pttCh2JoyButton = config.PttCh2JoyButton;
                        _pttCh3JoyGuid = config.PttCh3JoyGuid ?? "";
                        _pttCh3JoyButton = config.PttCh3JoyButton;

                        // 2. Update UI Buttons (Prioritize Keyboard, then Joystick, then Unbound)
                        
                        // Global TX
                        if (_pttVirtualKeys.Count > 0) 
                            PttBindButton.Content = $"[ {string.Join(" + ", _pttVirtualKeys.Select(k => KeyInterop.KeyFromVirtualKey(k).ToString()))} ]";
                        else if (_pttJoyButton >= 0) 
                            PttBindButton.Content = $"[ JOY {_pttJoyButton} ]";
                        else 
                            PttBindButton.Content = "[ UNBOUND ]"; // Or "[ V ]" if you prefer a strict default

                        // Channel 1
                        if (_pttCh1Keys.Count > 0) 
                            PttCh1BindButton.Content = $"[ {string.Join(" + ", _pttCh1Keys.Select(k => KeyInterop.KeyFromVirtualKey(k).ToString()))} ]";
                        else if (_pttCh1JoyButton >= 0) 
                            PttCh1BindButton.Content = $"[ JOY {_pttCh1JoyButton} ]";
                        else 
                            PttCh1BindButton.Content = "[ UNBOUND ]";

                        // Channel 2
                        if (_pttCh2Keys.Count > 0) 
                            PttCh2BindButton.Content = $"[ {string.Join(" + ", _pttCh2Keys.Select(k => KeyInterop.KeyFromVirtualKey(k).ToString()))} ]";
                        else if (_pttCh2JoyButton >= 0) 
                            PttCh2BindButton.Content = $"[ JOY {_pttCh2JoyButton} ]";
                        else 
                            PttCh2BindButton.Content = "[ UNBOUND ]";

                        // Channel 3
                        if (_pttCh3Keys.Count > 0) 
                            PttCh3BindButton.Content = $"[ {string.Join(" + ", _pttCh3Keys.Select(k => KeyInterop.KeyFromVirtualKey(k).ToString()))} ]";
                        else if (_pttCh3JoyButton >= 0) 
                            PttCh3BindButton.Content = $"[ JOY {_pttCh3JoyButton} ]";
                        else 
                            PttCh3BindButton.Content = "[ UNBOUND ]";
                    }
                } 
                catch { /* If file is corrupted, it just falls back to XAML defaults */ }
            }
        }

        private void SaveConfig()
        {
            var config = new AppConfig 
            {
                TargetIp = IpTextBox.Text,
                HostServer = HostServerCheckbox.IsChecked == true,
                PttKeys = _pttVirtualKeys,
                PttCh1Keys = _pttCh1Keys,
                PttCh2Keys = _pttCh2Keys,
                PttCh3Keys = _pttCh3Keys,
                PttJoyGuid = _pttJoyGuid,
                PttCh1JoyGuid = _pttCh1JoyGuid,
                PttCh2JoyGuid = _pttCh2JoyGuid,
                PttCh3JoyGuid = _pttCh3JoyGuid,
                PttJoyButton = _pttJoyButton,
                PttCh1JoyButton = _pttCh1JoyButton,
                PttCh2JoyButton = _pttCh2JoyButton,
                PttCh3JoyButton = _pttCh3JoyButton
            };
            
            File.WriteAllText(_configPath, JsonSerializer.Serialize(config));
        }
        // --- RADIO POWER & NETWORK INIT ---

        private void PowerSwitch_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _isRadioOn = !_isRadioOn;

            if (_isRadioOn)
            {
                PowerSwitch.Source = new BitmapImage(new Uri("assets/buttons/turnswitch_on.png", UriKind.Relative));
                LedOffline.Visibility = Visibility.Hidden;
                LedLive.Visibility = Visibility.Visible;
                StationLine1.Text = "READY";
                StationLine1.Foreground = System.Windows.Media.Brushes.Black;
                PlaySfx(SfxPowerOn);
                if (_playStatic) _staticPlayer.Play();

                // 1. Resolve Target IP from Settings immediately
                try
                {
                    IPEndPoint targetEndpoint = ResolveTargetAddress(IpTextBox.Text);
                    _currentIp = targetEndpoint.Address.ToString(); // This forces it to be a pure numeric IP
                    _currentPort = targetEndpoint.Port;
                }
                catch (Exception)
                {
                    // Fail gracefully if domain doesn't exist or is typed wrong
                    _isRadioOn = false;
                    PowerSwitch.Source = new BitmapImage(new Uri("assets/buttons/turnswitch_off.png", UriKind.Relative));
                    LedConn.Source = new BitmapImage(new Uri("assets/buttons/light_red.png", UriKind.Relative));
                    LedConn.Visibility = Visibility.Visible;
                    StationLine1.Text = "OFFLINE";
                    StationLine2.Text = "BAD ADDRESS";
                    return; 
                }

                // 2. Start Server if checked
                if (HostServerCheckbox.IsChecked == false) 
                {
                    LedConn.Source = new BitmapImage(new Uri("assets/buttons/light_yellow.png", UriKind.Relative));
                    LedConn.Visibility = Visibility.Visible;
                    StationLine2.Text = "CONNECTING...";
                    _connLedTimer.Start();
                }
                else
                {
                    _connLedTimer.Stop(); 
                    LedConn.Source = new BitmapImage(new Uri("assets/buttons/light_green.png", UriKind.Relative));
                    LedConn.Visibility = Visibility.Visible;
                    StationLine2.Text = "DISPATCH MODE";
                    try 
                    { 
                        _server = new RadioServer(_currentPort); 
                        _server.OnUPnPResult += (success) => 
                        {
                            Dispatcher.Invoke(() => 
                            {
                                if (_isRadioOn && HostServerCheckbox.IsChecked == true)
                                {
                                    if (success) StationLine2.Text = "DISPATCH[OPEN]";
                                    else StationLine2.Text = "DISPATCH[FAIL]";
                                }
                            });
                        };
                    } 
                    catch { /* Port taken */ }
                }

                // 3. Start Client & Audio Engine
                try 
                {
                    // Now safely passes the numeric IP
                    _client = new RadioClient(this, _currentIp, _currentPort);
                    _audioEngine = new AudioEngine();
                    _client.OnPongReceived += () => 
                    {
                        Dispatcher.Invoke(() => 
                        {
                            if (_isRadioOn && HostServerCheckbox.IsChecked == false)
                            {
                                // Only update the light and timer here
                                LedConn.Source = new BitmapImage(new Uri("assets/buttons/light_green.png", UriKind.Relative));
                                _connLedTimer.Stop(); 
                                _connLedTimer.Start(); 
                            }
                        });
                    };

                    _client.OnPingUpdated += (ms) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            // Keeps it tidy: "CONNECTED [45MS]"
                            // We only update if the radio is on and we aren't in a "FAIL" state
                            if (_isRadioOn && StationLine2.Text != "FAIL" && StationLine2.Text != "DISPATCH MODE" && HostServerCheckbox.IsChecked == false)
                            {
                                StationLine2.Text = $"CONNECTED [{ms}MS]";
                            }
                        });
                    };
                }
                catch (Exception)
                {
                    // If the IP is mangled or the domain doesn't exist, fail gracefully
                    _isRadioOn = false;
                    PowerSwitch.Source = new BitmapImage(new Uri("assets/buttons/turnswitch_off.png", UriKind.Relative));
                    LedConn.Source = new BitmapImage(new Uri("assets/buttons/light_red.png", UriKind.Relative));
                    LedConn.Visibility = Visibility.Visible;
                    StationLine1.Text = "OFFLINE";
                    StationLine2.Text = "BAD ADDRESS";
                    return; // Stop running the rest of the Power ON logic
                }

                // 4. Wire the Network <-> Audio pipeline
                _client.OnAudioReceived += (channelId, opusData) => 
                {
                    _audioEngine.ReceiveNetworkAudio(channelId, opusData);
                    Dispatcher.Invoke(() => 
                    {
                        LedRx.Visibility = Visibility.Visible;
                        _rxLedTimer.Stop();
                        _rxLedTimer.Start(); // Creates the 200ms flicker
                    });
                };

                _audioEngine.OnVolumeProcessed += (volume) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        // Clamp the volume so the LED is always at least a little visible when receiving
                        // but glows bright on loud peaks.
                        double brightness = Math.Clamp(volume * 5.0, 0.2, 1.0); 
                        LedRx.Opacity = brightness;
                    });
                };
                _audioEngine.OnEncodedPacketReady += (opusData) => { if (_isRadioOn) _client.TransmitAudio(opusData); };
            }
            else // Power OFF
            {
                _isRadioOn = false;
                _connLedTimer.Stop(); // Stop the 5s countdown immediately
                
                PowerSwitch.Source = new BitmapImage(new Uri("assets/buttons/turnswitch_off.png", UriKind.Relative));
                // Explicitly set the LED to nothing (Hidden) or Red if you prefer
                LedConn.Visibility = Visibility.Hidden;
                LedOffline.Visibility = Visibility.Visible;
                LedLive.Visibility = Visibility.Hidden;
                StationLine1.Text = "";
                StationLine2.Text = "";
                
                _server?.Shutdown(); // Now this actually works!
                _server = null;
                
                _client?.Shutdown();
                _client = null;

                _audioEngine?.Shutdown();
                _audioEngine = null;

                PlaySfx(SfxPowerOff);
                _staticPlayer.Pause();
            }
        }

        // --- PTT LOGIC ---

        private void CheckPttKey(object? sender, EventArgs e)
        {
            // If we are actively binding a key, check for joystick inputs and bind them!
            if (_bindingState != 0)
            {
                var joyPress = _inputManager.GetAnyJoystickButtonPressed();
                if (joyPress != null)
                {
                    if (_bindingState == 1) { _pttVirtualKeys.Clear(); _pttJoyGuid = joyPress.Value.Guid; _pttJoyButton = joyPress.Value.Button; PttBindButton.Content = $"[ JOY {joyPress.Value.Button} ]"; }
                    else if (_bindingState == 2) { _pttCh1Keys.Clear(); _pttCh1JoyGuid = joyPress.Value.Guid; _pttCh1JoyButton = joyPress.Value.Button; PttCh1BindButton.Content = $"[ JOY {joyPress.Value.Button} ]"; }
                    else if (_bindingState == 3) { _pttCh2Keys.Clear(); _pttCh2JoyGuid = joyPress.Value.Guid; _pttCh2JoyButton = joyPress.Value.Button; PttCh2BindButton.Content = $"[ JOY {joyPress.Value.Button} ]"; }
                    else if (_bindingState == 4) { _pttCh3Keys.Clear(); _pttCh3JoyGuid = joyPress.Value.Guid; _pttCh3JoyButton = joyPress.Value.Button; PttCh3BindButton.Content = $"[ JOY {joyPress.Value.Button} ]"; }
                    
                    _bindingState = 0; // Stop binding
                }
                return; 
            }

            if (!_isRadioOn) return;

            // Check if EITHER the keyboard bind OR the joystick bind is pressed for each channel
            bool isGlobal = _inputManager.IsKeyboardBoundAndPressed(_pttVirtualKeys) || _inputManager.IsJoystickBoundAndPressed(_pttJoyGuid, _pttJoyButton);
            bool isCh1 = _inputManager.IsKeyboardBoundAndPressed(_pttCh1Keys) || _inputManager.IsJoystickBoundAndPressed(_pttCh1JoyGuid, _pttCh1JoyButton);
            bool isCh2 = _inputManager.IsKeyboardBoundAndPressed(_pttCh2Keys) || _inputManager.IsJoystickBoundAndPressed(_pttCh2JoyGuid, _pttCh2JoyButton);
            bool isCh3 = _inputManager.IsKeyboardBoundAndPressed(_pttCh3Keys) || _inputManager.IsJoystickBoundAndPressed(_pttCh3JoyGuid, _pttCh3JoyButton);

            bool anyPressed = isGlobal || isCh1 || isCh2 || isCh3;
    

            if (anyPressed && !_isTransmitting)
            {
                _isTransmitting = true;
                
                if (isCh1) ActiveOverrideChannel = 1;
                else if (isCh2) ActiveOverrideChannel = 2;
                else if (isCh3) ActiveOverrideChannel = 3;
                else ActiveOverrideChannel = null;

                if (_playBlip) PlaySfx(SfxBlip);
                _staticPlayer.Pause();
                
                StationLine1.Text = ActiveOverrideChannel.HasValue ? $"TX CH {ActiveOverrideChannel}" : "TRANSMITTING";
                LedTx.Visibility = Visibility.Visible;
                StationLine1.Foreground = System.Windows.Media.Brushes.Black;
                _audioEngine?.StartTransmitting();
            }
            else if (!anyPressed && _isTransmitting)
            {
                _isTransmitting = false;
                ActiveOverrideChannel = null;
                
                if (_playBlip) PlaySfx(SfxBlip);
                if (_playStatic && _isRadioOn) _staticPlayer.Play();
                
                StationLine1.Text = "READY";
                LedTx.Visibility = Visibility.Hidden;
                StationLine1.Foreground = System.Windows.Media.Brushes.Black;
                _audioEngine?.StopTransmitting();
            }
        }

        // --- SETTINGS OVERLAY ---

        private void ParamButton_Click(object sender, RoutedEventArgs e) => SettingsOverlay.Visibility = Visibility.Visible;

        private void CloseSettings_Click(object sender, RoutedEventArgs e)
        {
            SaveConfig();
            SettingsOverlay.Visibility = Visibility.Collapsed;
            _isBindingPtt = false; 
        }

        private void HostServerCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            if (HostServerCheckbox.IsChecked == true)
            {
                string localIp = "127.0.0.1";
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork) { localIp = ip.ToString(); break; }
                }

                IpTextBox.Text = $"{localIp}:5000";
                IpTextBox.IsReadOnly = true;
                IpTextBox.Foreground = System.Windows.Media.Brushes.DarkGray;
                CopyIpButton.Visibility = Visibility.Visible;
            }
            else
            {
                IpTextBox.IsReadOnly = false;
                IpTextBox.Foreground = System.Windows.Media.Brushes.White;
                CopyIpButton.Visibility = Visibility.Collapsed;
            }
        }

        private void CopyIpButton_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(IpTextBox.Text);
            CopyIpButton.Content = "COPIED";
            Task.Delay(2000).ContinueWith(_ => Dispatcher.Invoke(() => CopyIpButton.Content = "COPY"));
        }

        private void PttBindButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                // Tag determines which list we are binding to (0/null = Global, 1 = Ch1, etc.)
                _bindingState = btn.Tag != null ? int.Parse(btn.Tag.ToString()) + 1 : 1;
                
                // Clear the target list
                if (_bindingState == 1) _pttVirtualKeys.Clear();
                else if (_bindingState == 2) _pttCh1Keys.Clear();
                else if (_bindingState == 3) _pttCh2Keys.Clear();
                else if (_bindingState == 4) _pttCh3Keys.Clear();

                btn.Content = "PRESS KEYS (ESC TO CLEAR)";
                this.Focus(); 
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (_bindingState != 0)
            {
                List<int> targetList = _bindingState switch { 1 => _pttVirtualKeys, 2 => _pttCh1Keys, 3 => _pttCh2Keys, 4 => _pttCh3Keys, _ => new List<int>() };
                Button targetButton = _bindingState switch { 1 => PttBindButton, 2 => PttCh1BindButton, 3 => PttCh2BindButton, 4 => PttCh3BindButton, _ => PttBindButton };

                // 1. Clear Bind Logic (Escape Key)
                if (e.Key == Key.Escape)
                {
                    targetList.Clear(); // Wipe keyboard bind
                    
                    // Wipe joystick bind
                    if (_bindingState == 1) { _pttJoyGuid = ""; _pttJoyButton = -1; }
                    else if (_bindingState == 2) { _pttCh1JoyGuid = ""; _pttCh1JoyButton = -1; }
                    else if (_bindingState == 3) { _pttCh2JoyGuid = ""; _pttCh2JoyButton = -1; }
                    else if (_bindingState == 4) { _pttCh3JoyGuid = ""; _pttCh3JoyButton = -1; }

                    targetButton.Content = "[ UNBOUND ]";
                    _bindingState = 0;
                    return;
                }

                // 2. Keyboard Binding Logic
                int vk = KeyInterop.VirtualKeyFromKey(e.Key);
                if (!targetList.Contains(vk)) targetList.Add(vk);

                targetButton.Content = $"[ {string.Join(" + ", targetList.Select(k => KeyInterop.KeyFromVirtualKey(k).ToString()))} ]";
                
                // 3. Prevent Ghosting: If they just bound a keyboard key, delete the joystick bind for this channel
                if (_bindingState == 1) { _pttJoyGuid = ""; _pttJoyButton = -1; }
                else if (_bindingState == 2) { _pttCh1JoyGuid = ""; _pttCh1JoyButton = -1; }
                else if (_bindingState == 3) { _pttCh2JoyGuid = ""; _pttCh2JoyButton = -1; }
                else if (_bindingState == 4) { _pttCh3JoyGuid = ""; _pttCh3JoyButton = -1; }
                
                _bindingState = 0; // Stop binding once a key is pressed
            }
        }

        private void Window_KeyUp(object sender, KeyEventArgs e)
        {
            if (_bindingState != 0) _bindingState = 0; // Stop binding when keys are released
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if ((e.Key == Key.System && e.SystemKey == Key.F4) || 
                (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Escape))
            {
                _client?.Shutdown();
                _audioEngine?.Shutdown();
                Application.Current.Shutdown();
            }
        }

        private void ChannelSwitch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton toggle && toggle.Tag is string tag)
            {
                bool isActive = toggle.IsChecked == true;
                
                // Play the physical click sound
                PlaySfx(isActive ? SfxSwitchOn : SfxSwitchOff);

                string[] parts = tag.Split('_');
                string category = parts[0];

                // 1. Handle the Non-Numbered Switches (Misc)
                if (category == "Misc")
                {
                    if (parts[1] == "Blip") 
                    {
                        _playBlip = isActive;
                    }
                    else if (parts[1] == "Static") 
                    {
                        _playStatic = isActive;
                        if (_playStatic && _isRadioOn && !_isTransmitting) 
                            _staticPlayer.Play();
                        else 
                            _staticPlayer.Pause();
                    }
                    return; // Stop here so we don't try to parse "Blip" as a number!
                }

                // 2. Handle the Numbered Channels (Tx/Rx)
                if (category == "Rx" || category == "Tx")
                {
                    // Now it is perfectly safe to parse the number
                    if (byte.TryParse(parts[1], out byte channelId))
                    {
                        if (category == "Rx")
                        {
                            if (isActive) ActiveRxChannels.Add(channelId);
                            else ActiveRxChannels.Remove(channelId);
                        }
                        else if (category == "Tx")
                        {
                            if (isActive) ActiveTxChannels.Add(channelId);
                            else ActiveTxChannels.Remove(channelId);
                        }
                    }
                }
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) this.DragMove();
        }

        private void PlaySfx(string path)
        {
            // Re-opening the URI allows it to interrupt itself and play immediately
            _sfxPlayer.Open(new Uri(path));
            _sfxPlayer.Play();
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            // Dynamically reads the version from your .csproj properties
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            
            string aboutText = $"XMIT501 Tactical Radio\n" +
                            $"Version: {version}\n\n" +
                            $"Developed by Red Sun\n" +
                            $"Lightweight VoIP for Tactical Operations.\n\n" +
                            $"Network: UDP Peer-to-Peer\n" +
                            $"This app is free and open source.\n" +
                            $"It is not tracking anything or collecting any data.\n\n";

            MessageBox.Show(aboutText, "ABOUT XMIT501", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    
        private IPEndPoint ResolveTargetAddress(string addressString)
        {
            // Clean up the string just in case someone pastes "sftp://" or spaces
            addressString = addressString.Replace("sftp://", "").Replace("udp://", "").Trim();

            string[] parts = addressString.Split(':');
            if (parts.Length != 2) throw new FormatException("Must be formatted as ADDRESS:PORT");

            string host = parts[0];
            int port = int.Parse(parts[1]);

            // Scenario A: It's a standard IP (127.0.0.1)
            if (IPAddress.TryParse(host, out IPAddress ip))
            {
                return new IPEndPoint(ip, port);
            }

            // Scenario B: It's a Domain Name (zeus.hidencloud.com)
            var addresses = System.Net.Dns.GetHostAddresses(host);
            
            // Grab the first IPv4 address we find
            var ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (ipv4 != null)
            {
                return new IPEndPoint(ipv4, port);
            }

            throw new Exception("Could not resolve domain name.");
        }
    }

    public class AppConfig
    {
        public string TargetIp { get; set; } = "127.0.0.1:5000";
        public bool HostServer { get; set; } = false;
        
        // Keyboard Binds
        public List<int> PttKeys { get; set; } = new List<int> { 0x56 }; 
        public List<int> PttCh1Keys { get; set; } = new List<int>(); 
        public List<int> PttCh2Keys { get; set; } = new List<int>(); 
        public List<int> PttCh3Keys { get; set; } = new List<int>(); 
        
        // Joystick Binds (Guid string and Button integer)
        public string PttJoyGuid { get; set; } = "";
        public int PttJoyButton { get; set; } = -1;
        
        public string PttCh1JoyGuid { get; set; } = "";
        public int PttCh1JoyButton { get; set; } = -1;
        
        public string PttCh2JoyGuid { get; set; } = "";
        public int PttCh2JoyButton { get; set; } = -1;
        
        public string PttCh3JoyGuid { get; set; } = "";
        public int PttCh3JoyButton { get; set; } = -1;
    }
}