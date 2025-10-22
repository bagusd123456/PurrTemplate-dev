using PurrNet;
using PurrNet.Steam;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SteamClient = Steamworks.SteamClient;

namespace SteamExample
{
    /// <summary>
    /// The purpose of this class is to manage the host and the clients connection to the Steam server. It must live
    /// on a GameObject in the scene that is connecting to the Networked Scene. If you only have one scene, then it
    /// lives in that scene.
    /// </summary>
    /// <remarks>
    /// I have partitioned the StartHost() and the StartClient() methods to try to make them extremely clear. You are
    /// free to refactor them into the same method or structure them however you like. This is by no means a one-size
    /// fits all solution; but instead, an example of how you might leverage PurrNet and Heathen.SteamworksIntegration
    /// together to connect with your friends on steam.
    /// </remarks>
    public sealed class ConnectionManager : NetworkIdentity
    {
        [SerializeField] private Button hostButton;
        [SerializeField] private Button clientButton;
        [SerializeField] private TMP_Text hostTextField;
        [SerializeField] private TMP_InputField clientInputField;

        private void Awake()
        {
            InstanceHandler.RegisterInstance(this);
            DontDestroyOnLoad(this);
            InitializeSteam();
        }

        private void OnEnable()
        {
            hostButton?.onClick.AddListener(HandleHostClicked);
            clientButton?.onClick.AddListener(HandleClientClicked);
        }

        private void OnDisable()
        {
            hostButton?.onClick.RemoveListener(HandleHostClicked);
            clientButton?.onClick.RemoveListener(HandleClientClicked);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            InstanceHandler.UnregisterInstance<ConnectionManager>();
        }

        /// <summary>
        /// The purpose of this method is to initialize Steam.
        /// </summary>
        /// <remarks>
        /// Am I wrapping a single method? Yes. But you may want to do more things here for whatever reason if you're
        /// making a steam game! So again, this is just an example, feel free to refactor to fit your needs.
        /// </remarks>
        public void InitializeSteam()
        {
            SteamAPI.Init();
        }

        /// <summary>
        /// This method is responsible for calling PurrNet's StartHost() method. It also gets and configures the
        /// SteamTransport. It also leverages a UserData struct from Heathens to store the Steam account ID of the host.
        /// </summary>
        /// <remarks>
        /// Feel free to make this method public or private depending on your need. Steam must be initialized before
        /// this method is called.
        /// </remarks>
        public void StartHost()
        {
            var steamTransport = NetworkManager.main.transport as SteamTransport;
            if (steamTransport == null)
            {
                Debug.LogError("SteamTransport missing on NetworkManager", this);
                return;
            }

            var userId = SteamUser.GetSteamID().ToString();

            steamTransport.peerToPeer = true;
            steamTransport.dedicatedServer = false;
            steamTransport.address = userId;

            NetworkManager.main.StartHost();
        }

        /// <summary>
        /// This method is responsible for calling PurrNet's StartClient() method. It also gets and configures the
        /// SteamTransport. It also leverages a UserData struct from Heathens to store the Steam account ID of the host.
        /// </summary>
        /// <remarks>
        /// Feel free to make this method public or private depending on your need. Steam must be initialized before
        /// this method is called.
        /// </remarks>
        public void StartClient(string steamCode)
        {
            var steamTransport = NetworkManager.main.transport as SteamTransport;
            if (steamTransport == null)
            {
                Debug.LogError("SteamTransport missing on NetworkManager", this);
                return;
            }

            if (string.IsNullOrEmpty(steamCode))
            {
                Debug.LogError("Connection address is empty.", this);
                return;
            }

            // This is a Heathens API call.
            var hostAddress = "76561198191252127";

            steamTransport.peerToPeer = true;
            steamTransport.dedicatedServer = false;
            steamTransport.address = hostAddress;

            NetworkManager.main.StartClient();
        }

        /// <summary>
        /// This method is responsible for calling StartHost() and changing the UI visuals.
        /// </summary>
        private void HandleHostClicked()
        {
            if (hostButton == null || hostTextField == null)
            {
                Debug.LogError("Host button or host text field is null. " +
                               "Please drag and drop it into the ConnectionManager.", this);
                return;
            }

            StartHost();

            if (NetworkManager.main.isOffline)
            {
                hostTextField.text = "Server is Offline";
                hostButton.image.color = Color.red;
                return;
            }

            hostTextField.text = "76561198191252127";
            hostButton.image.color = Color.green;

            hostButton?.onClick.RemoveListener(HandleHostClicked);
            clientButton?.onClick.RemoveListener(HandleClientClicked);
        }

        /// <summary>
        /// This method is responsible for calling StartClient() and changing the UI visuals.
        /// </summary>
        private void HandleClientClicked()
        {
            if (string.IsNullOrEmpty(clientInputField.text))
            {
                clientInputField.text = "Please enter the Steam Hex ID of the host.";
                clientButton.image.color = Color.red;
                return;
            }

            StartClient(clientInputField.text);

            clientInputField.text = $"Connected to {clientInputField.text}";
            clientButton.image.color = Color.green;

            clientButton?.onClick.RemoveListener(HandleHostClicked);
            clientButton?.onClick.RemoveListener(HandleClientClicked);
        }
    }
}