using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

/// <summary>Represents a server discovered on the LAN.</summary>
public struct ServerEntry
{
    public string hostName;
    public string ip;
    public int    currentPlayers;
    public int    maxPlayers;
    public float  lastSeen;
}

/// <summary>
/// LANDiscovery — handles both advertising a hosted game (host side)
/// and discovering available games (client side) over UDP broadcast.
///
/// Attach to a DontDestroyOnLoad GameObject in the main menu scene.
/// </summary>
public class LANDiscovery : MonoBehaviour
{
    public static LANDiscovery Instance { get; private set; }

    // Discovery uses a dedicated port that doesn't collide with Mirror's game port (7777)
    private const int   DISCOVERY_PORT     = 47778;
    private const float BROADCAST_INTERVAL = 2f;
    private const float SERVER_TIMEOUT     = 7f;

    // ── State ─────────────────────────────────────────────────────────
    private UdpClient advertiseSocket;
    private UdpClient listenSocket;
    private bool      advertising = false;
    private bool      browsing    = false;

    private string advertiseHostName;
    private int    advertiseCurrent;
    private int    advertiseMax;

    /// <summary>Live list of servers discovered on the LAN.</summary>
    public List<ServerEntry> Servers { get; } = new List<ServerEntry>();

    /// <summary>Fired on the main thread whenever the server list changes.</summary>
    public event Action OnServersChanged;

    // ── Lifecycle ─────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        StopAdvertising();
        StopBrowsing();
    }

    // ── Host API ──────────────────────────────────────────────────────

    /// <summary>Begin broadcasting this server's presence on the LAN.</summary>
    public void StartAdvertising(string hostName, int currentPlayers, int maxPlayers)
    {
        StopBrowsing();

        advertiseHostName = hostName;
        advertiseCurrent  = currentPlayers;
        advertiseMax      = maxPlayers;

        try
        {
            advertiseSocket                 = new UdpClient();
            advertiseSocket.EnableBroadcast = true;
            advertising                     = true;
            StartCoroutine(AdvertiseLoop());
        }
        catch (Exception e)
        {
            Debug.LogError("[LANDiscovery] Failed to open advertise socket: " + e.Message);
        }
    }

    /// <summary>Update the player count shown in browser listings.</summary>
    public void UpdatePlayerCount(int currentPlayers) => advertiseCurrent = currentPlayers;

    /// <summary>Stop broadcasting.</summary>
    public void StopAdvertising()
    {
        advertising = false;
        try { advertiseSocket?.Close(); } catch { }
        advertiseSocket = null;
    }

    // ── Client API ────────────────────────────────────────────────────

    /// <summary>Begin listening for server broadcasts.</summary>
    public void StartBrowsing()
    {
        StopAdvertising();
        Servers.Clear();
        browsing = true;

        try
        {
            listenSocket                 = new UdpClient(DISCOVERY_PORT);
            listenSocket.EnableBroadcast = true;
            StartCoroutine(ListenLoop());
            StartCoroutine(TimeoutLoop());
        }
        catch (Exception e)
        {
            Debug.LogError("[LANDiscovery] Failed to open listen socket: " + e.Message);
            browsing = false;
        }
    }

    /// <summary>Stop listening and clear the server list.</summary>
    public void StopBrowsing()
    {
        browsing = false;
        try { listenSocket?.Close(); } catch { }
        listenSocket = null;
        Servers.Clear();
    }

    // ── Coroutines ────────────────────────────────────────────────────

    private IEnumerator AdvertiseLoop()
    {
        var endpoint = new IPEndPoint(IPAddress.Broadcast, DISCOVERY_PORT);
        while (advertising)
        {
            try
            {
                // Format: hostName|currentPlayers|maxPlayers
                string msg  = $"{advertiseHostName}|{advertiseCurrent}|{advertiseMax}";
                byte[] data = Encoding.UTF8.GetBytes(msg);
                advertiseSocket?.Send(data, data.Length, endpoint);
            }
            catch { /* socket closed mid-send is fine */ }
            yield return new WaitForSeconds(BROADCAST_INTERVAL);
        }
    }

    private IEnumerator ListenLoop()
    {
        while (browsing && listenSocket != null)
        {
            if (listenSocket.Available > 0)
            {
                try
                {
                    var    remote = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data   = listenSocket.Receive(ref remote);
                    string msg    = Encoding.UTF8.GetString(data);
                    ProcessMessage(msg, remote.Address.ToString());
                }
                catch { }
            }
            yield return null;
        }
    }

    private IEnumerator TimeoutLoop()
    {
        while (browsing)
        {
            float now     = Time.time;
            bool  changed = Servers.RemoveAll(s => now - s.lastSeen > SERVER_TIMEOUT) > 0;
            if (changed) OnServersChanged?.Invoke();
            yield return new WaitForSeconds(1f);
        }
    }

    // ── Parsing ───────────────────────────────────────────────────────

    private void ProcessMessage(string msg, string senderIP)
    {
        // Ignore our own broadcasts (same machine)
        if (senderIP == GetLocalIP() || senderIP == "127.0.0.1") return;

        string[] parts = msg.Split('|');
        if (parts.Length < 3) return;

        if (!int.TryParse(parts[1], out int cp)) cp = 0;
        if (!int.TryParse(parts[2], out int mp)) mp = 2;

        var entry = new ServerEntry
        {
            hostName       = parts[0],
            currentPlayers = cp,
            maxPlayers     = mp,
            ip             = senderIP,
            lastSeen       = Time.time
        };

        bool found = false;
        for (int i = 0; i < Servers.Count; i++)
        {
            if (Servers[i].ip != entry.ip) continue;
            Servers[i] = entry;
            found = true;
            break;
        }
        if (!found) Servers.Add(entry);

        OnServersChanged?.Invoke();
    }

    // ── Utility ───────────────────────────────────────────────────────

    /// <summary>Returns the local LAN IP used for outbound connections.</summary>
    public static string GetLocalIP()
    {
        try
        {
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram,
                                     ProtocolType.Udp);
            s.Connect("8.8.8.8", 65530);
            return ((IPEndPoint)s.LocalEndPoint).Address.ToString();
        }
        catch { return "127.0.0.1"; }
    }
}
