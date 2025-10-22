using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "DiscordConfig", menuName = "Discord Config", order = 100)]
public class DiscordConfig : ScriptableObject {
    public ulong applicationId;
}