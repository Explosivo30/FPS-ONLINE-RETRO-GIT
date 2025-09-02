using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

public class MainMenu : MonoBehaviour
{
    
    public async void StartHost()
    {
        await HostSingleton.Instance.GameManager.StartHostAsync();
    }



}
