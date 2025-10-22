using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Discord.Sdk;
using UnityEngine;
using UnityEngine.UIElements;

public class VoiceSettingsController : IDisposable {
    private Client client_;
    private SampleViewSettings settings_;
    private Call activeCall_;
    private bool isDisposed_;

    private VisualElement rootVisual_;
    private VisualElement mobileControls_;
    private Toggle pttToggle_;
    private Button audioRoutePickerButton_;

    class PortState {
        public DropdownField deviceDropdown;
        public Slider volumeSlider;
        public string selectedDevice;
        public float volume;
        public List<string> deviceIds;
        public Action<string> onDeviceSelected;
        public Action<float> onVolumeChanged;
        private bool isLoading_;

        public void Register() {
            deviceDropdown.RegisterValueChangedCallback((evt) => {
                if (isLoading_) {
                    return;
                }
                Debug.Log(
                  $"Selected device {deviceDropdown.index} ({deviceIds[deviceDropdown.index]})");
                selectedDevice = deviceIds[deviceDropdown.index];
                onDeviceSelected(selectedDevice);
            });
            volumeSlider.value = volume;
            volumeSlider.RegisterValueChangedCallback((evt) => {
                if (isLoading_) {
                    return;
                }
                volume = volumeSlider.value;
                onVolumeChanged(volume);
            });
        }

        public void BeginLoad() {
            isLoading_ = true;
            deviceDropdown.SetEnabled(false);
            deviceDropdown.choices = new List<string>();
            deviceDropdown.value = "Loading...";
        }

        public void EndLoad(AudioDevice[] devicesRaw) {
            Debug.Log($"Received devices @ {DateTime.Now}");
            using var dispose = devicesRaw.AsDisposable();
            deviceIds.Clear();
            int i = 0;
            foreach (var device in devicesRaw) {
                var deviceId = device.Id();
                var deviceName = device.Name();
                deviceDropdown.choices.Add(deviceName);
                deviceIds.Add(deviceId);
                if (deviceId == selectedDevice) {
                    deviceDropdown.index = i;
                    deviceDropdown.value = deviceName;
                }
                i++;
            }
            deviceDropdown.SetEnabled(true);
            isLoading_ = false;
        }
    }

    private PortState inputPortState_;
    private PortState outputPortState_;

    public event Action OnClosed;

    public VoiceSettingsController(Client client,
                                   SampleViewSettings settings,
                                   Call activeCall,
                                   VisualElement rootVisual) {
        client_ = client;
        settings_ = settings;
        activeCall_ = activeCall;
        rootVisual_ = rootVisual;
        inputPortState_ =
          new PortState { deviceDropdown = rootVisual_.Q<DropdownField>("inputDeviceDropdown"),
                          volumeSlider = rootVisual_.Q<Slider>("inputVolumeSlider"),
                          selectedDevice = settings_.selectedInputDevice,
                          volume = settings_.inputVolume,
                          deviceIds = new List<string>(),
                          onDeviceSelected =
                            (deviceId) => {
                                settings_.selectedInputDevice = deviceId;
                                client_.SetInputDevice(deviceId, (result) => {});
                            },
                          onVolumeChanged =
                            (volume) => {
                                settings_.inputVolume = volume;
                                client_.SetInputVolume(volume);
                            } };
        outputPortState_ =
          new PortState { deviceDropdown = rootVisual_.Q<DropdownField>("outputDeviceDropdown"),
                          volumeSlider = rootVisual_.Q<Slider>("outputVolumeSlider"),
                          selectedDevice = settings_.selectedOutputDevice,
                          volume = settings_.outputVolume,
                          deviceIds = new List<string>(),
                          onDeviceSelected =
                            (deviceId) => {
                                settings_.selectedOutputDevice = deviceId;
                                client_.SetOutputDevice(deviceId, (result) => {});
                            },
                          onVolumeChanged =
                            (volume) => {
                                settings_.outputVolume = volume;
                                client_.SetOutputVolume(volume);
                            } };
        inputPortState_.Register();
        outputPortState_.Register();
        mobileControls_ = rootVisual_.Q<VisualElement>("mobileControls");
#if UNITY_IOS || UNITY_ANDROID
        mobileControls_.style.display = DisplayStyle.Flex;
#else
        mobileControls_.style.display = DisplayStyle.None;
#endif
        pttToggle_ = rootVisual_.Q<Toggle>("pttToggle");
        pttToggle_.value = settings_.pttEnabled;
        pttToggle_.RegisterValueChangedCallback((evt) => {
            settings_.pttEnabled = pttToggle_.value;
            if (activeCall_ == null) {
                return;
            }
            activeCall_.SetAudioMode(settings_.pttEnabled ? AudioModeType.MODE_PTT
                                                          : AudioModeType.MODE_VAD);
        });
        audioRoutePickerButton_ = rootVisual_.Q<Button>("audioRoutePickerButton");
        audioRoutePickerButton_.clicked += () => { client_.ShowAudioRoutePicker(); };
        RefreshDevices();
    }

    private void RefreshDevices() {
        Debug.Log($"Requested devices @ {DateTime.Now}");
        inputPortState_.BeginLoad();
        outputPortState_.BeginLoad();
        client_.GetOutputDevices((devices) => {
            if (!isDisposed_) {
                outputPortState_.EndLoad(devices);
            }
        });
        client_.GetInputDevices((devices) => {
            if (!isDisposed_) {
                inputPortState_.EndLoad(devices);
            }
        });
    }

    public void Dispose() {
        if (!isDisposed_) {
            Debug.Log("VoiceSettingsController disposed");
            isDisposed_ = true;
            OnClosed?.Invoke();
        }
    }
}
