using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class LobbyLoadingPanel : View
{
    [SerializeField] private TMP_Text loadingText;
    public void Set(string inputString = "Loading...")
    {
        loadingText.text = inputString;
    }
}
