using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Discord.Sdk;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UIElements;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

// Friends list filters approximating the Discord UI definitions, e.g. "All" is
// all of your actual friends, not including pending/blocked.
public enum FriendsListFilter { Online, All, Pending, Blocked }

public enum HomeListEntryType {
    FriendsList,
    Header,
    Lobby,
    JoinRequest,
    JoinInvite,
    UnityLogs,
    DiscordLogs,
    DiscordConnectedUserInfo,
}

public class HomeListEntry {
    public HomeListEntryType Type { get; set; }
    public ulong Id { get; set; }
    public string Name { get; set; }
    public IDisposable Handle { get; set; }
    public Action AddAction { get; set; }

    public bool IsSame(HomeListEntry other) {
        return other != null && Type == other.Type && Id == other.Id;
    }
}

public class Friend : IDisposable {
    public RelationshipHandle Relationship { get; }
    public UserHandle User { get; }

    public bool MatchesFilter(FriendsListFilter filter) {
        var relType = Relationship.DiscordRelationshipType();
        switch (filter) {
        case FriendsListFilter.Online:
            return relType == RelationshipType.Friend &&
              (User.Status()
                 is StatusType.Online or StatusType.Idle or StatusType.Dnd or StatusType.Streaming);
        case FriendsListFilter.All:
            return relType == RelationshipType.Friend;
        case FriendsListFilter.Pending:
            return relType is RelationshipType.PendingIncoming or RelationshipType.PendingOutgoing;
        case FriendsListFilter.Blocked:
            return relType == RelationshipType.Blocked;
        default:
            return false;
        }
    }

    public Friend(RelationshipHandle rel) {
        Relationship = rel;
        User = rel.User();
    }

    public void Dispose() {
        Relationship.Dispose();
        User?.Dispose();
    }
}

public class Message : IDisposable {
    public MessageHandle Handle { get; }
    public Guid? LocalId { get; set; }
    public ulong? Id { get; set; }
    public string Content { get; set; }
    public string SenderName { get; set; }
    public Message(Client client, MessageHandle handle) {
        Handle = handle;
        UpdateFromHandle(client);
    }
    public Message(string senderName, string content) {
        LocalId = Guid.NewGuid();
        Content = content;
        SenderName = senderName;
    }
    public void Dispose() { Handle?.Dispose(); }
    public void UpdateFromHandle(Client client) {
        if (Handle == null) {
            return;
        }
        Id = Handle.Id();
        Content = Handle.Content();
        using var user = client.GetUser(Handle.AuthorId());
        if (user != null) {
            SenderName = user.Username();
        }
    }
}

[Serializable]
public class SampleViewSettings {
    public string token;
    public Dictionary<ulong, string> lobbySecrets;
    public string selectedOutputDevice = "default";
    public string selectedInputDevice = "default";
    public float outputVolume = 100.0f;
    public float inputVolume = 100.0f;
    public bool pttEnabled = false;
}

public enum LogCategory { DiscordPartnerSdk, Unity }

public class LogMessage {
    public LogType Type { get; set; }
    public string Text { get; set; }
    public string StackTrace { get; set; }
}

public class SampleView : MonoBehaviour {
    public DiscordConfig config;

    // Templates
    [SerializeField]
    private VisualTreeAsset friendsListTemplate;

    [SerializeField]
    private VisualTreeAsset friendsListEntryTemplate;

    [SerializeField]
    private VisualTreeAsset lobbyViewTemplate;

    [SerializeField]
    private VisualTreeAsset inviteFriendModalTemplate;

    [SerializeField]
    private VisualTreeAsset joinLobbyTemplate;

    [SerializeField]
    private VisualTreeAsset voiceSettingsTemplate;

    // The thing.
    private Client client_;
    private Call activeCall_;

    // OAuth2 state
    private string codeVerifier_;
    private string oauth2State_;

    // UI elements: root view
    private VisualElement root_;
    private Label currentStatus_;
    private Button connectButton_;
    private Button getTokenButton_;
    private Button disconnectButton_;
    private Button annoyModeButton_;
    private TextField token_;
    private ScrollView homeListView_;
    private VisualElement contentWell_;
    private VisualElement modal_;

    // UI elements: voice control cluster
    private VisualElement voiceControls_;
    private VisualElement voiceChannelControls_;
    private Label voiceStatusText_;
    private Label voiceChannelText_;
    private Label voiceUsernameText_;
    private Button pttActiveButton_;
    private Button voiceDisconnectButton_;
    private Button muteButton_;
    private Button deafenButton_;
    private Button settingsButton_;

    // UI state
    private SampleViewSettings settings_;
    private List<HomeListEntry> homeListEntries_ = new List<HomeListEntry>();
    private HomeListEntry selectedHomeListEntry_;
    private FriendsListFilter friendsListFilter_;
    private Dictionary<ulong, List<Message>> messages_ = new();
    private Dictionary<Guid, Message> pendingMessages_ = new();
    private Dictionary<ulong, string> lobbySecrets_ = new();
    private List<ActivityInvite> activityInvites_ = new();
    private Dictionary<LogCategory, List<LogMessage>> logs_ = new();
    // Retained friend handles for friend list content well and invite views
    private DisposableArray<Friend>? friendsList_;
    private DisposableArray<Friend>? invitedFriendsList_;
    private bool messageListPendingScrollDown_;
    private bool annoyModeEnabled_;
    private IDisposable modalController_;
    private ImageLoader imageLoader_ = new();
    private UserHandle discordClientConnectedUser_;

    private void OpenModal(VisualElement content, IDisposable controller = null) {
        CloseModal();
        modalController_ = controller;
        var modal = new VisualElement();
        var wrapper = new VisualElement();
        var background = new VisualElement();
        background.AddToClassList("modal-background");
        modal.AddToClassList("modal");
        modal.RegisterCallback<PointerDownEvent>((evt) => {
            if (evt.target == background) {
                evt.StopImmediatePropagation();
                CloseModal();
            }
        });
        wrapper.AddToClassList("modal-content");
        wrapper.Add(content);
        modal.Add(background);
        modal.Add(wrapper);
        modal_ = modal;
        var uiDocument = GetComponent<UIDocument>();
        uiDocument.rootVisualElement.Add(modal_);
    }

    private void CloseModal() {
        modalController_?.Dispose();
        modalController_ = null;
        modal_?.RemoveFromHierarchy();
        modal_ = null;
    }

    void Update() {
        var audioSource = GetComponent<AudioSource>();
        if (audioSource == null) {
            return;
        }
        if (annoyModeEnabled_) {
            if (!audioSource.isPlaying) {
                audioSource.Play();
            }
        } else {
            audioSource.Stop();
        }
    }

#region Initialization
    IEnumerator Start() {
        yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone)) {
            Permission.RequestUserPermission(Permission.Microphone);
        }
#endif
    }

    private void OnEnable() {
        var uiDocument = GetComponent<UIDocument>();
        var root = uiDocument.rootVisualElement;
        root_ = root;
        root_.RegisterCallback<GeometryChangedEvent>((evt) => { UpdateSafeArea(); });
        currentStatus_ = root.Q<Label>("currentStatus");
        connectButton_ = root.Q<Button>("connectButton");
        getTokenButton_ = root.Q<Button>("getTokenButton");
        disconnectButton_ = root.Q<Button>("disconnectButton");
        annoyModeButton_ = root.Q<Button>("annoyModeButton");
        homeListView_ = root.Q<ScrollView>("homeList");
        token_ = root.Q<TextField>("token");
        contentWell_ = root.Q<VisualElement>("contentWell");
        voiceControls_ = root.Q<VisualElement>("voiceControls");
        voiceChannelControls_ = voiceControls_.Q<VisualElement>("channelControls");
        pttActiveButton_ = voiceControls_.Q<Button>("pttActiveButton");
        voiceStatusText_ = voiceControls_.Q<Label>("status");
        voiceUsernameText_ = voiceControls_.Q<Label>("username");
        voiceChannelText_ = voiceControls_.Q<Label>("channelName");
        voiceDisconnectButton_ = voiceControls_.Q<Button>("disconnectButton");
        muteButton_ = voiceControls_.Q<Button>("muteButton");
        deafenButton_ = voiceControls_.Q<Button>("deafenButton");
        settingsButton_ = voiceControls_.Q<Button>("settingsButton");
        connectButton_.clicked += OnConnectClicked;
        disconnectButton_.clicked += OnDisconnectClicked;
        getTokenButton_.clicked += OnGetTokenClicked;
        voiceDisconnectButton_.clicked += OnVoiceDisconnectClicked;
        muteButton_.clicked += OnMuteClicked;
        deafenButton_.clicked += OnDeafenClicked;
        settingsButton_.clicked += OnSettingsClicked;
        annoyModeButton_.clicked += OnAnnoyModeClicked;
#if !UNITY_ANDROID && !UNITY_IOS
        annoyModeButton_.style.display = DisplayStyle.None;
#endif
        pttActiveButton_.RegisterCallback<PointerUpEvent>(OnPttActiveUp, TrickleDown.TrickleDown);
        pttActiveButton_.RegisterCallback<PointerDownEvent>(OnPttActiveDown,
                                                            TrickleDown.TrickleDown);
        Application.logMessageReceived += OnUnityLogMessageReceived;
        ResetClient();
        LoadSettings();
        RenderHomeList();
    }

    private void UpdateSafeArea() {
        var scale = GetComponent<UIDocument>().panelSettings.scale;
        var safeArea = Screen.safeArea;
        root_.style.paddingTop = safeArea.y / scale;
        root_.style.paddingBottom = (Screen.height - safeArea.yMax) / scale;
        root_.style.paddingLeft = safeArea.x / scale;
        root_.style.paddingRight = (Screen.width - safeArea.xMax) / scale;
    }

    private void OnDisable() {
        Application.logMessageReceived -= OnUnityLogMessageReceived;
        client_.Disconnect();
    }

    private void OnDestroy() {
        client_?.Dispose();
        client_ = null;
        imageLoader_.Dispose();
    }

    private void OnUnityLogMessageReceived(string condition, string stackTrace, LogType type) {
        logs_.Activate(LogCategory.Unity, () => new List<LogMessage>())
          .Add(new LogMessage { Type = type, Text = condition, StackTrace = stackTrace });
    }

    private void LoadSettings() {
        var settings = PlayerPrefs.HasKey("SampleView")
          ? JsonConvert.DeserializeObject<SampleViewSettings>(PlayerPrefs.GetString("SampleView"))
          : null;
        if (settings == null) {
            settings = new SampleViewSettings();
        }
        settings_ = settings;
        token_.value = settings.token ?? "";
        lobbySecrets_ = settings.lobbySecrets ?? new Dictionary<ulong, string>();
        client_.SetOutputDevice(settings.selectedOutputDevice, (result) => {});
        client_.SetInputDevice(settings.selectedInputDevice, (result) => {});
    }

    private void SaveSettings() {
        settings_.token = token_.value;
        settings_.lobbySecrets = lobbySecrets_;
        Debug.Log($"Saving settings with output device {settings_.selectedOutputDevice} " +
                  $"and input device {settings_.selectedInputDevice}");
        PlayerPrefs.SetString("SampleView", JsonConvert.SerializeObject(settings_));
        PlayerPrefs.Save();
    }

    private void ResetClient() {
        if (client_ != null) {
            client_.Disconnect();
        } else {
            var options = new ClientCreateOptions();
            // some fun voice options you can try.
            // options.SetExperimentalAudioSystem(AudioSystem.Game);
            // options.SetExperimentalAndroidPreventCommsForBluetooth(true);
            client_ = new Client(options);
        }
        client_.UpdateToken(AuthorizationTokenType.Bearer, token_.text, (result) => {});
        client_.SetEchoCancellation(true);
        client_.SetAutomaticGainControl(true);
        client_.SetNoiseSuppression(true);
        client_.SetStatusChangedCallback(OnStatusChanged);
        client_.SetRelationshipCreatedCallback(OnRelationshipChanged);
        client_.SetRelationshipDeletedCallback(OnRelationshipChanged);
        client_.SetUserUpdatedCallback(OnUserChanged);
        client_.SetLobbyCreatedCallback(OnLobbyCreated);
        client_.SetLobbyDeletedCallback(OnLobbyDeleted);
        client_.SetLobbyMemberAddedCallback(OnLobbyMemberChanged);
        client_.SetLobbyMemberUpdatedCallback(OnLobbyMemberChanged);
        client_.SetLobbyMemberRemovedCallback(OnLobbyMemberChanged);
        client_.SetActivityInviteCreatedCallback(OnActivityInvite);
        client_.SetMessageCreatedCallback(OnMessageCreated);
        client_.SetMessageUpdatedCallback(OnMessageUpdated);
        client_.AddLogCallback(OnDiscordLogMessageReceived, LoggingSeverity.Verbose);
        FetchDiscordClientConnectedUser();
        RenderStatus();
    }
#endregion

#region Client events
    private void OnStatusChanged(Client.Status status, Client.Error error, int errorDetail) {
        Debug.Log($"[Discord] SetStatusChangedCallback {status} {error} {errorDetail}");
        if (client_ == null) {
            return;
        }
        switch (status) {
        case Client.Status.Ready:
            OnReady();
            break;
        case Client.Status.Disconnected:
            OnDisconnected();
            break;
        }
        RenderStatus();
    }

    private void OnReady() {
        SaveSettings();
        SetRichPresence(null);
        RenderHomeList();
        RenderContentWell();
    }

    private void OnDisconnected() {
        RenderHomeList();
        RenderContentWell();
    }

    private void OnRelationshipChanged(ulong userId, bool isDiscordRelationship) {
        if (client_ == null) {
            return;
        }
        RenderFriendsList();
    }

    private void OnUserChanged(ulong userId) {
        if (client_ == null) {
            return;
        }
        RenderFriendsList();
        RenderStatus();
    }

    private void OnPresenceChanged(ulong userId) {
        if (client_ == null) {
            return;
        }
        RenderFriendsList();
    }

    private void OnLobbyCreated(ulong lobbyId) {
        if (client_ == null) {
            return;
        }
        RenderHomeList();
    }

    private void OnLobbyDeleted(ulong lobbyId) {
        if (client_ == null) {
            return;
        }
        RenderHomeList();
        if (lobbyId == selectedHomeListEntry_?.Id &&
            selectedHomeListEntry_.Type == HomeListEntryType.Lobby) {
            SetSelectedHomeListEntry(null);
        }
    }

    private void OnLobbyMemberChanged(ulong lobbyId, ulong userId) {
        if (client_ == null) {
            return;
        }
        Debug.Log($"Lobby member changed: {lobbyId} {userId}");
        RenderMemberList();
    }

    private void OnLobbyJoined(ulong lobbyId, string secret, ClientResult result) {
        if (client_ == null) {
            return;
        }
        Debug.Log($"Lobby join result: {lobbyId} {secret} {result.Status()} {result.Error()}");
        lobbySecrets_[lobbyId] = secret;
        SaveSettings();
        RenderHomeList();
        SetSelectedHomeListEntry(homeListEntries_.FirstOrDefault(
          e => e.Id == lobbyId && e.Type == HomeListEntryType.Lobby));
    }

    private void OnActivityInvite(ActivityInvite invite) {
        if (client_ == null) {
            return;
        }
        Debug.Log($"Activity invite: {invite.SenderId()} {invite.Type()}");
        activityInvites_.Add(invite);
        RenderHomeList();
    }

    private void OnActivityInviteAccepted(ClientResult result, string joinSecret) {
        if (client_ == null) {
            return;
        }
        Debug.Log(
          $"Activity invite accept result: [{joinSecret}] [{result.Status()}] [{result.Error()}]");
        if (result.Status() == Discord.Sdk.HttpStatusCode.Ok) {
            CreateOrJoinLobby(joinSecret);
        }
        result.Dispose();
    }

    private void OnMessageCreated(ulong messageId) {
        if (client_ == null) {
            return;
        }
        Debug.Log($"Message created: {messageId}");
        using var handle = client_.GetMessageHandle(messageId);
        if (handle == null) {
            return;
        }
        var channelId = handle.ChannelId();
        var message = new Message(client_, handle);
        var messages = messages_.Activate(channelId, () => new List<Message>());
        messages.Add(message);
        if (selectedHomeListEntry_?.Type == HomeListEntryType.Lobby &&
            selectedHomeListEntry_.Id == channelId) {
            var messageList = contentWell_.Q<ScrollView>("messageList");
            RenderMessage(messageList, message, true);
        }
    }

    private void OnMessageSent(Discord.Sdk.ClientResult result, ulong messageId) {
        if (client_ == null) {
            return;
        }
        Debug.Log($"Message sent: [{messageId}] [{result.Status()}] [{result.Error()}]");
        result.Dispose();
    }

    private void OnMessageUpdated(ulong messageId) {
        if (client_ == null) {
            return;
        }
        Debug.Log($"Message updated: {messageId}");
    }

    private void OnDiscordLogMessageReceived(string message, LoggingSeverity severity) {
        if (client_ == null) {
            return;
        }
        var type = severity switch { LoggingSeverity.Warning => LogType.Warning,
                                     LoggingSeverity.Error => LogType.Error,
                                     _ => LogType.Log };
        logs_.Activate(LogCategory.DiscordPartnerSdk, () => new List<LogMessage>())
          .Add(new LogMessage { Type = type, Text = message });
    }
#endregion

#region Status bar / voice controls
    private void RenderStatus() {
        var status = client_.GetStatus();
        if (status == Client.Status.Ready) {
            using var user = client_.GetCurrentUserV2();
            if (user != null) {
                var name = user.Username();
                currentStatus_.text = $"Ready ({name})";
            } else {
                currentStatus_.text = "Ready (No user info)";
            }
            voiceUsernameText_.text = name;
        } else {
            currentStatus_.text = client_.GetStatus().ToString();
        }
        if (activeCall_ == null) {
            voiceChannelControls_.style.display = DisplayStyle.None;
            pttActiveButton_.style.display = DisplayStyle.None;
            return;
        }
        muteButton_.EnableInClassList("voice-control-active", activeCall_.GetSelfMute());
        deafenButton_.EnableInClassList("voice-control-active", activeCall_.GetSelfDeaf());
        pttActiveButton_.style.display =
          settings_.pttEnabled ? DisplayStyle.Flex : DisplayStyle.None;
        voiceChannelControls_.style.display = DisplayStyle.Flex;
        voiceStatusText_.text = activeCall_.GetStatus().ToString();
        var channelId = activeCall_.GetChannelId();
        if (lobbySecrets_.TryGetValue(channelId, out var secret)) {
            voiceChannelText_.text = secret;
        } else {
            voiceChannelText_.text = channelId.ToString();
        }
    }

    private void OnConnectClicked() {
        client_.UpdateToken(
          AuthorizationTokenType.Bearer, token_.text, (result) => { client_.Connect(); });
    }

    private void OnDisconnectClicked() { ResetClient(); }

    private void OnVoiceDisconnectClicked() {
        if (activeCall_ != null) {
            client_.EndCalls(() => {});
            activeCall_.Dispose();
            activeCall_ = null;
        }
        RenderStatus();
    }

    private void OnMuteClicked() {
        if (activeCall_ != null) {
            activeCall_.SetSelfMute(!activeCall_.GetSelfMute());
        }
        RenderStatus();
    }

    private void OnDeafenClicked() {
        if (activeCall_ != null) {
            activeCall_.SetSelfDeaf(!activeCall_.GetSelfDeaf());
        }
        RenderStatus();
    }

    private void OnSettingsClicked() {
        var content = voiceSettingsTemplate.CloneTree();
        var controller = new VoiceSettingsController(client_, settings_, activeCall_, content);
        controller.OnClosed += () => {
            SaveSettings();
            RenderStatus();
        };
        OpenModal(content, controller);
    }

    private void OnAnnoyModeClicked() { annoyModeEnabled_ = !annoyModeEnabled_; }

    private void OnPttActiveUp(PointerUpEvent evt) {
        Debug.Log("OnPttActiveUp");
        if (activeCall_ != null) {
            activeCall_.SetPTTActive(false);
        }
    }

    private void OnPttActiveDown(PointerDownEvent evt) {
        Debug.Log("OnPttActiveDown");
        if (activeCall_ != null) {
            activeCall_.SetPTTActive(true);
        }
    }

    private void FetchDiscordClientConnectedUser() {
        client_.GetDiscordClientConnectedUser(config.applicationId, (result, user) => {
            discordClientConnectedUser_ = user;
            RenderContentWell();
        });
    }
#endregion

#region OAuth2
    private static string UrlSafeBase64Encode(byte[] data) {
        return Convert.ToBase64String(data).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private void GetTokenFromCode(string code, string redirectUri) {
        var client = client_;
        if (client == null) {
            return;
        }

        client.GetToken(config.applicationId,
                        code,
                        codeVerifier_,
                        redirectUri,
                        (result, token, refreshToken, tokenType, expiresIn, scope) => {
                            if (token != "") {
                                OnReceivedToken(token);
                            } else {
                                OnRetrieveTokenFailed();
                            }
                        });
    }

    private void OnReceivedToken(string token) {
        token_.value = token;
        client_.UpdateToken(
          AuthorizationTokenType.Bearer, token, (result) => { client_.Connect(); });
    }

    private void OnRetrieveTokenFailed() { token_.value = "Failed to retrieve token"; }

    private void OnGetTokenClicked() {
        var args = new AuthorizationArgs();
        var codeVerifier = client_.CreateAuthorizationCodeVerifier();
        codeVerifier_ = codeVerifier.Verifier();
        args.SetClientId(config.applicationId);
        args.SetScopes(Client.GetDefaultCommunicationScopes());
        args.SetCodeChallenge(codeVerifier.Challenge());
        client_.Authorize(args, OnAuthorizeResult);
    }

    private void OnAuthorizeResult(ClientResult result, string code, string redirectUri) {
        Debug.Log($"Authorization result: [{result.Error()}] [{code}] [{redirectUri}]");
        if (!result.Successful()) {
            return;
        }
        GetTokenFromCode(code, redirectUri);
    }
#endregion

#region Home list
    private void AddHomeListEntry(HomeListEntry entry) { homeListEntries_.Add(entry); }

    private void RenderHomeList() {
        RefreshHomeList();
        homeListView_.Clear();
        foreach (var entry in homeListEntries_) {
            switch (entry.Type) {
            case HomeListEntryType.Header:
                var header = new Label(entry.Name);
                var headerContainer = new VisualElement();
                headerContainer.AddToClassList("list-section-header");
                headerContainer.Add(header);
                if (entry.AddAction != null) {
                    var addButton = new Button(entry.AddAction) { text = "+" };
                    headerContainer.Add(addButton);
                }
                homeListView_.Add(headerContainer);
                break;
            case HomeListEntryType.FriendsList:
            case HomeListEntryType.Lobby:
            case HomeListEntryType.JoinRequest:
            case HomeListEntryType.JoinInvite:
            case HomeListEntryType.UnityLogs:
            case HomeListEntryType.DiscordLogs:
            case HomeListEntryType.DiscordConnectedUserInfo:
                homeListView_.Add(
                  new Button(() => OnHomeListEntryClicked(entry)) { text = entry.Name });
                break;
            }
        }
    }

    private void RefreshHomeList() {
        foreach (var entry in homeListEntries_) {
            entry.Handle?.Dispose();
        }
        homeListEntries_.Clear();
        if (client_.GetStatus() == Client.Status.Ready) {
            AddOnlineHomeListEntries();
        }
        AddHomeListEntry(
          new HomeListEntry { Type = HomeListEntryType.Header, Name = "Debug Logs" });
        AddHomeListEntry(
          new HomeListEntry { Type = HomeListEntryType.DiscordLogs, Name = "Discord" });
        AddHomeListEntry(new HomeListEntry { Type = HomeListEntryType.UnityLogs, Name = "Unity" });
        AddHomeListEntry(new HomeListEntry { Type = HomeListEntryType.DiscordConnectedUserInfo,
                                             Name = "Discord Client Connected User" });
        bool replacedSelectedEntry = false;
        foreach (var entry in homeListEntries_) {
            if (entry.IsSame(selectedHomeListEntry_)) {
                selectedHomeListEntry_ = entry;
                replacedSelectedEntry = true;
                break;
            }
        }
        if (!replacedSelectedEntry) {
            selectedHomeListEntry_ = null;
        }
    }

    private void AddOnlineHomeListEntries() {
        AddHomeListEntry(
          new HomeListEntry { Type = HomeListEntryType.FriendsList, Name = "Friends" });
        AddHomeListEntry(new HomeListEntry {
            Type = HomeListEntryType.Header, Name = "Lobbies", AddAction = OnAddLobbyClicked
        });
        var lobbies = client_.GetLobbyIds();
        foreach (var lobbyId in lobbies) {
            var handle = client_.GetLobbyHandle(lobbyId);
            string name;
            if (lobbySecrets_.TryGetValue(lobbyId, out var secret)) {
                name = $"{secret}";
            } else {
                name = lobbyId.ToString();
            }
            AddHomeListEntry(new HomeListEntry {
                Type = HomeListEntryType.Lobby, Id = handle.Id(), Name = name, Handle = handle
            });
        }
        var requests = activityInvites_.ToLookup(ar => ar.Type());
        foreach (var group in requests) {
            var items = group.ToArray();
            if (items.Length > 0) {
                var type = items[0].Type();
                var name = type switch { ActivityActionTypes.JoinRequest => "Join Requests",
                                         ActivityActionTypes.Join => "Invites",
                                         _ => "Unknown" };
                AddHomeListEntry(
                  new HomeListEntry { Type = HomeListEntryType.Header, Name = name });
                foreach (var item in items) {
                    using var user = client_.GetUser(item.SenderId());
                    AddHomeListEntry(new HomeListEntry {
                        Type =
                          type switch { ActivityActionTypes.JoinRequest =>
                                          HomeListEntryType.JoinRequest,
                                        ActivityActionTypes.Join => HomeListEntryType.JoinInvite,
                                        _ => throw new InvalidOperationException() },
                        Name = user?.Username() ?? "Unknown", Id = item.SenderId()
                    });
                }
            }
        }
    }

    private void OnAddLobbyClicked() {
        var content = joinLobbyTemplate.CloneTree();
        var joinButton = content.Q<Button>("joinButton");
        var roomName = content.Q<TextField>("roomName");
        joinButton.clicked += () => {
            var secret = roomName.value;
            CreateOrJoinLobby(secret);
            CloseModal();
        };
        OpenModal(content);
    }

    private void CreateOrJoinLobby(string secret) {
        Debug.Log($"CreateOrJoinLobby: {secret}");
        var userMetadata = new Dictionary<string, string>();
        var lobbyMetadata = new Dictionary<string, string>();
        using var me = client_.GetCurrentUserV2();
        if (me != null) {
            lobbyMetadata["creator"] = $"{me.Id()}";
            userMetadata["displayName"] = me.DisplayName();
        } else {
            Debug.LogError("Cannot create lobby: no current user available");
            return;
        }
        userMetadata["avatarUrl"] =
          me.AvatarUrl(UserHandle.AvatarType.Png, UserHandle.AvatarType.Png);
        client_.CreateOrJoinLobbyWithMetadata(secret,
                                              lobbyMetadata,
                                              userMetadata,
                                              (apiResult, lobbyId) =>
                                                OnLobbyJoined(lobbyId, secret, apiResult));
    }

    private void SetSelectedHomeListEntry(HomeListEntry entry) {
        selectedHomeListEntry_ = entry;
        RenderContentWell();
        if (entry?.Type == HomeListEntryType.Lobby) {
            SetRichPresence(entry.Id);
            contentWell_.Q<TextField>("messageInput").Focus();
        } else if (entry?.Type == HomeListEntryType.DiscordConnectedUserInfo) {
            FetchDiscordClientConnectedUser();
        } else {
            SetRichPresence(null);
        }
    }

    private void SetRichPresence(ulong? lobbyId) {
        using var lobbyHandle = lobbyId != null ? client_.GetLobbyHandle(lobbyId.Value) : null;
        var activity = new Activity();
        activity.SetName("Discord x Unity");
        activity.SetType(ActivityTypes.Playing);
        if (lobbyHandle != null && lobbySecrets_.ContainsKey(lobbyId.Value)) {
            var activityParty = new ActivityParty();
            var partySize = lobbyHandle.LobbyMemberIds().Length;
            activityParty.SetId(lobbyId.Value.ToString());
            activityParty.SetCurrentSize(partySize);
            activityParty.SetMaxSize(99);
            var secrets = new ActivitySecrets();
            secrets.SetJoin(lobbySecrets_[lobbyId.Value]);
            activity.SetState($"Lobby ({partySize}/99)");
            activity.SetParty(activityParty);
            activity.SetSecrets(secrets);
        }
        activity.SetSupportedPlatforms(ActivityGamePlatforms.Desktop | ActivityGamePlatforms.IOS);
        client_.UpdateRichPresence(activity, (result) => {
            Debug.Log($"UpdateRichPresence: {result.Successful()} {result.Error()}");
        });
    }

    private void OnHomeListEntryClicked(HomeListEntry entry) {
        switch (entry.Type) {
        case HomeListEntryType.JoinInvite:
            var invite = activityInvites_.FirstOrDefault(ar => ar.SenderId() == entry.Id &&
                                                           ar.Type() == ActivityActionTypes.Join);
            client_.AcceptActivityInvite(invite, OnActivityInviteAccepted);
            activityInvites_.RemoveAll(ar => invite.SenderId() == entry.Id &&
                                         ar.Type() == ActivityActionTypes.Join);
            RenderHomeList();
            break;
        case HomeListEntryType.JoinRequest:
            Debug.Log("TODO: Accept join request");
            break;
        default:
            SetSelectedHomeListEntry(entry);
            break;
        }
    }
#endregion

#region Content well
    private void RenderContentWell() {
        contentWell_.Clear();
        if (selectedHomeListEntry_ == null) {
            return;
        }
        switch (selectedHomeListEntry_.Type) {
        case HomeListEntryType.FriendsList:
            friendsListTemplate.CloneTree(contentWell_);
            BindFriendsListFilters();
            RenderFriendsList();
            break;
        case HomeListEntryType.Lobby:
            lobbyViewTemplate.CloneTree(contentWell_);
            var lobbyTitle = contentWell_.Q<Label>("lobbyTitle");
            lobbyTitle.text = $"Lobby [{selectedHomeListEntry_.Name}]";
            RegisterLobbyViewEvents();
            RenderMessageList();
            RenderMemberList();
            break;
        case HomeListEntryType.UnityLogs:
        case HomeListEntryType.DiscordLogs:
            var logCategory = selectedHomeListEntry_.Type == HomeListEntryType.UnityLogs
              ? LogCategory.Unity
              : LogCategory.DiscordPartnerSdk;
            var logList = new ScrollView();
            logList.AddToClassList("log-list");
            if (logs_.TryGetValue(logCategory, out var messages)) {
                foreach (var message in messages) {
                    var label = new Label($"{message.Type}: {message.Text} {message.StackTrace}");
                    label.AddToClassList("log-message");
                    logList.Add(label);
                }
            }
            logList.schedule.Execute(
              () => { logList.verticalScroller.value = logList.verticalScroller.highValue; });
            contentWell_.Add(logList);
            break;
        case HomeListEntryType.DiscordConnectedUserInfo:
            var userContent = new VisualElement();
            if (discordClientConnectedUser_ == null) {
                userContent.Add(new Label("No user detected"));
            } else {
                userContent.Add(new Label($"Username: {discordClientConnectedUser_.Username()}"));
                userContent.Add(new Label($"Id: {discordClientConnectedUser_.Id()}"));
            }
            contentWell_.Add(userContent);
            break;
        }
    }

    private void BindFriendsListFilters() {
        var filterButtons = contentWell_.Q<VisualElement>("friendsListFilters");
        filterButtons.Clear();
        foreach (var filter in Enum.GetValues(typeof(FriendsListFilter))
                   .Cast<FriendsListFilter>()) {
            filterButtons.Add(
              new Button(() => OnFriendsListFilterClicked(filter)) { text = filter.ToString() });
        }
    }

    private void RenderFriendsList() {
        if (selectedHomeListEntry_?.Type != HomeListEntryType.FriendsList) {
            return;
        }
        var friendListView = contentWell_.Q<ScrollView>("friendsList");
        friendsList_?.Dispose();
        friendsList_ =
          client_.GetRelationships().Select(r => new Friend(r)).ToArray().AsDisposable();
        var displayedFriends =
          friendsList_.Value.ToArray()
            .Where(f => f.MatchesFilter(friendsListFilter_))
            .OrderBy(f =>
                       f.User.DisplayName().Length > 0 ? f.User.DisplayName() : f.User.GlobalName())
            .ToArray();
        friendListView.Clear();
        foreach (var friend in displayedFriends) {
            friendsListEntryTemplate.CloneTree(friendListView.contentContainer);
            var last = friendListView[friendListView.childCount - 1];
            var avatar = last.Q<Image>("avatar");
            var displayName = last.Q<Label>("displayName");
            var presence = last.Q<Label>("presence");
            var backgroundButton = last.Q<Button>("backgroundButton");
            var dmButton = last.Q<Button>("dmButton");
            var moreButton = last.Q<Button>("moreButton");
            displayName.text = friend.User.DisplayName();
            presence.text = friend.User.Status().ToString() ?? "Offline";
            LoadImageAsync(
              avatar, friend.User.AvatarUrl(UserHandle.AvatarType.Png, UserHandle.AvatarType.Png))
              .LogException();
            backgroundButton.clicked += () => OnFriendClicked(friend);
            dmButton.clicked += () => OnFriendClicked(friend);
            moreButton.clicked += () => OnFriendClicked(friend);
            dmButton.visible = false;
        }
    }

    private async Task LoadImageAsync(Image imageElement, string uri) {
        try {
            imageElement.image = await imageLoader_.Load(uri);
        } catch (HttpRequestException e) {
            Debug.Log($"Failed to load image: {e}");
        }
    }

    private void OnFriendClicked(Friend friend) {
        OpenFriendActionsModal(
          friend.User.Id(), friend.User.Username(), friend.Relationship.DiscordRelationshipType());
    }

    private void OpenFriendActionsModal(ulong userId, string username, RelationshipType relType) {
        var content = new VisualElement();
        var hasAddFriend = false;
        var hasAcceptFriend = false;
        var hasDeclineFriend = false;
        var hasBlock = false;
        var hasUnblock = false;
        var hasRemoveFriend = false;
        switch (relType) {
        case RelationshipType.Implicit:
        case RelationshipType.Suggestion:
        case RelationshipType.None:
            hasAddFriend = true;
            break;
        case RelationshipType.Friend:
            hasRemoveFriend = true;
            hasBlock = true;
            break;
        case RelationshipType.Blocked:
            hasUnblock = true;
            break;
        case RelationshipType.PendingIncoming:
            hasAcceptFriend = true;
            hasDeclineFriend = true;
            break;
        case RelationshipType.PendingOutgoing:
            hasRemoveFriend = true;
            break;
        }
        Client.UpdateRelationshipCallback onRelUpdate = (result) => {
            Debug.Log($"Relationship update: {result.Status()} {result.Error()}");
            RenderMemberList();
            RenderFriendsList();
        };
        Client.SendFriendRequestCallback onFriendRequestSent = (result) => {
            Debug.Log($"Friend request sent for {username}: {result.Status()} {result.Error()}");
            RenderFriendsList();
        };
        content.Add(new Label($"Friend Actions ({relType})"));
        if (hasAddFriend) {
            content.Add(new Button(() => {
                client_.SendDiscordFriendRequest(username, onFriendRequestSent);
                CloseModal();
            }) { text = "Add Friend" });
        }
        if (hasAcceptFriend) {
            content.Add(new Button(() => {
                client_.AcceptDiscordFriendRequest(userId, onRelUpdate);
                CloseModal();
            }) { text = "Accept Friend" });
        }
        if (hasBlock) {
            content.Add(new Button(() => {
                client_.BlockUser(userId, onRelUpdate);
                CloseModal();
            }) { text = "Block" });
        }
        if (hasUnblock) {
            content.Add(new Button(() => {
                client_.UnblockUser(userId, onRelUpdate);
                CloseModal();
            }) { text = "Unblock" });
        }
        if (hasRemoveFriend) {
            content.Add(new Button(() => {
                client_.RemoveDiscordAndGameFriend(userId, onRelUpdate);
                CloseModal();
            }) { text = "Remove Friend" });
        }
        if (hasDeclineFriend) {
            content.Add(new Button(() => {
                client_.RejectDiscordFriendRequest(userId, onRelUpdate);
                CloseModal();
            }) { text = "Decline Friend" });
        }
        OpenModal(content);
    }

    private void OnFriendsListFilterClicked(FriendsListFilter filter) {
        friendsListFilter_ = filter;
        RenderFriendsList();
    }

    private void RegisterLobbyViewEvents() {
        var inputBar = contentWell_.Q<VisualElement>("inputBar");
        var messageInput = contentWell_.Q<TextField>("messageInput");
        var messageList = contentWell_.Q<ScrollView>("messageList");
        var sendButton = contentWell_.Q<Button>("sendButton");
        var inviteFriendsButton = contentWell_.Q<Button>("inviteFriendsButton");
        var joinVoiceButton = contentWell_.Q<Button>("joinVoiceButton");
        inputBar.RegisterCallback<KeyDownEvent>((evt) => {
            if (evt.keyCode == KeyCode.Return || evt.character == '\n') {
                evt.StopImmediatePropagation();
                evt.PreventDefault();
            }
            if (evt.keyCode == KeyCode.Return && !evt.shiftKey) {
                OnSendMessageIntent(messageInput);
            }
        }, TrickleDown.TrickleDown);
        messageList.contentContainer.RegisterCallback<GeometryChangedEvent>((evt) => {
            if (messageListPendingScrollDown_) {
                messageList.verticalScroller.value = messageList.verticalScroller.highValue;
                messageListPendingScrollDown_ = false;
            }
        });
        sendButton.clicked += () => OnSendMessageIntent(messageInput);
        inviteFriendsButton.clicked += () => OnInviteFriendsButtonClicked();
        joinVoiceButton.clicked += () => OnJoinVoiceButtonClicked();
    }

    private void OnSendMessageIntent(TextField messageInput) {
        if (selectedHomeListEntry_?.Type != HomeListEntryType.Lobby) {
            return;
        }
        var message = messageInput.value;
        if (string.IsNullOrWhiteSpace(message)) {
            return;
        }
        messageInput.value = "";
        Debug.Log($"Send message: {message}");
        var lobbyId = selectedHomeListEntry_.Id;
        using var me = client_.GetCurrentUserV2();
        if (me == null) {
            Debug.LogError("Cannot send message: no current user available");
            return;
        }
        var pendingMessage = new Message(me.Username(), message);
        client_.SendLobbyMessage(lobbyId, message, OnMessageSent);
    }

    private void RenderMessageList() {
        if (selectedHomeListEntry_?.Type != HomeListEntryType.Lobby) {
            return;
        }
        var messageList = contentWell_.Q<ScrollView>("messageList");
        messageList.Clear();
        if (messages_.TryGetValue(selectedHomeListEntry_.Id, out var messages)) {
            foreach (var message in messages) {
                RenderMessage(messageList, message);
            }
        }
        messageListPendingScrollDown_ = true;
    }

    private void RenderMessage(ScrollView messageList, Message message, bool scroll = false) {
        var label = new Label($"<{message.SenderName}> {message.Content}");
        label.AddToClassList("message");
        if (message.LocalId != null && message.Handle == null) {
            label.AddToClassList("pending");
        }
        if (scroll) {
            messageListPendingScrollDown_ = true;
        }
        messageList.Add(label);
    }

    private void RenderMemberList() {
        if (selectedHomeListEntry_?.Type != HomeListEntryType.Lobby) {
            return;
        }
        var lobby = (LobbyHandle)selectedHomeListEntry_.Handle;
        var memberIds = lobby.LobbyMemberIds();
        var memberList = contentWell_.Q<ScrollView>("memberList");
        memberList.Clear();
        var lobbyMetadata = lobby.Metadata();
        foreach (var memberId in memberIds) {
            using var memberHandle = lobby.GetLobbyMemberHandle(memberId);
            using var user = client_.GetUser(memberId);
            using var voiceState = activeCall_?.GetVoiceStateHandle(memberId);
            var memberMetadata = memberHandle.Metadata();
            var username = user?.Username();
            var displayName = user != null
              ? user.DisplayName()
              : memberMetadata.GetValueOrDefault("displayName", "Unknown");
            var connected = memberHandle.Connected();
            var inVoice = voiceState != null;
            var isOwner = lobbyMetadata.GetValueOrDefault("creator") == memberId.ToString();
            var isMuted = (voiceState?.SelfMute() ?? false);
            var isDeafened = (voiceState?.SelfDeaf() ?? false);
            memberList.Add(new Button(() => {
                if (user != null) {
                    OnMemberClicked(memberId, username);
                }
            }) {
                text =
                  $"{(isOwner ? "[*] " : "")}{(inVoice ? "[V] " : "")}{displayName}{(!connected ? " [linkdead]" : "")}{(isMuted ? " [M]" : "")}{(isDeafened ? " [D]" : "")}"
            });
        }
    }

    private void OnMemberClicked(ulong memberId, string username) {
        using var rel = client_.GetRelationshipHandle(memberId);
        var relType = RelationshipType.None;
        if (rel != null) {
            relType = rel.DiscordRelationshipType();
        }
        OpenFriendActionsModal(memberId, username, relType);
    }

    private void OnInviteFriendsButtonClicked() {
        var lobbyId = selectedHomeListEntry_.Id;
        var content = inviteFriendModalTemplate.CloneTree();
        var friendsList = content.Q<ScrollView>("friendsList");
        var inviteButton = content.Q<Button>("inviteButton");
        invitedFriendsList_?.Dispose();
        invitedFriendsList_ =
          client_.GetRelationships().Select(r => new Friend(r)).ToArray().AsDisposable();
        var displayedFriends = invitedFriendsList_.Value.ToArray()
                                 .Where(f => f.MatchesFilter(FriendsListFilter.All))
                                 .ToArray();
        foreach (var friend in displayedFriends) {
            friendsList.Add(new Toggle(
              friend.User.Username()) { value = false, userData = friend.Relationship.Id() });
        }
        inviteButton.clicked += () => {
            var selectedFriendIds = friendsList.Children()
                                      .Where(c => c is Toggle t && t.value)
                                      .Select(c => (ulong)c.userData)
                                      .ToArray();
            DoInviteFriends(lobbyId, selectedFriendIds);
        };
        OpenModal(content);
    }

    private void OnJoinVoiceButtonClicked() {
        if (selectedHomeListEntry_?.Type != HomeListEntryType.Lobby) {
            return;
        }
        if (activeCall_ != null) {
            client_.EndCalls(() => {});
            activeCall_.Dispose();
            activeCall_ = null;
        }
        activeCall_ = client_.StartCall(selectedHomeListEntry_.Id);
        if (activeCall_ == null) {
            Debug.Log("Failed to create discord call.");
            return;
        }
        activeCall_.SetAudioMode(settings_.pttEnabled ? AudioModeType.MODE_PTT
                                                      : AudioModeType.MODE_VAD);
        activeCall_.SetStatusChangedCallback(OnCallStatusChanged);
        activeCall_.SetParticipantChangedCallback(OnParticipantChanged);
        activeCall_.SetOnVoiceStateChangedCallback(OnVoiceStateChanged);
        RenderStatus();
    }

    private void OnCallStatusChanged(Call.Status status, Call.Error error, int errorDetail) {
        Debug.Log($"Call status changed: {status} {error} {errorDetail}");
        RenderStatus();
        RenderMemberList();
    }

    private void OnParticipantChanged(ulong userId, bool speaking) {
        Debug.Log($"Participant changed: {userId} {speaking}");
        RenderMemberList();
    }

    private void OnVoiceStateChanged(ulong userId) {
        Debug.Log($"Voice state changed: {userId}");
        RenderMemberList();
    }

    private void DoInviteFriends(ulong lobbyId, ulong[] selectedFriendIds) {
        foreach (var friendId in selectedFriendIds) {
            client_.SendActivityInvite(friendId, "", (result) => {
                Debug.Log($"Invite sent: {friendId} {result.Status()} {result.Error()}");
            });
        }
        CloseModal();
    }
#endregion
}
