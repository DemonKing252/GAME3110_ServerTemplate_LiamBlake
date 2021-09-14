using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;
using UnityEngine.UI;

public class NetworkedServer : MonoBehaviour
{
    public int MaxConnections = 16;

    // Message is guaranteed however it may not be in order
    public int ReliableConnection;

    // Message is not guaranteed, may not be in order (thus the name 'unreliable')
    public int UnrealiableConnection;

    public int port = 5491;
    public int hostID;
    

    private void Start()
    {
        // TODO: 
        // 1. Establish a connection
        // 2. Print a message when a client joins

        TryConnection();

    }

    private void Update()
    {
        HandleMessages();
    }

    private void HandleMessages()
    {
        int recHostID;
        int recConnectionID;
        int recChannelID;
        byte[] recBuffer = new byte[1024];
        int bufferSize = 1024;
        int dataSize;
        byte error = 0;

        NetworkEventType recNetworkEvent = NetworkTransport.Receive(out recHostID, 
            out recConnectionID, 
            out recChannelID, 
            recBuffer, 
            bufferSize, 
            out dataSize, 
            out error);

        switch (recNetworkEvent)
        {
            case NetworkEventType.ConnectEvent:

                Debug.Log("Client connecting: NetId: " + recConnectionID.ToString());
                break;
            case NetworkEventType.DataEvent:
                string str = Encoding.ASCII.GetString(recBuffer);
                Debug.Log("Client says: " + str);

                string msg_back_to_client = "Hello client, we hope you brought pizza";

                // Sending message back to client using the same address that was recieved
                byte[] buffer = Encoding.ASCII.GetBytes(msg_back_to_client);
                NetworkTransport.Send(hostID, recConnectionID, ReliableConnection, buffer, buffer.Length, out error);


                break;
            case NetworkEventType.DisconnectEvent:

                Debug.Log("Client disconnecting: netId:" + recConnectionID.ToString());
                break;
        }
        

    }

    private void TryConnection()
    {
        NetworkTransport.Init();

        ConnectionConfig config = new ConnectionConfig();

        // https://docs.unity3d.com/ScriptReference/Networking.QosType.html

        // Quality of service: Messages are guaranteed, but may not be in order
        ReliableConnection = config.AddChannel(QosType.Reliable);

        // Quality of service: Messages are not guaranteed, and may not be in order
        UnrealiableConnection = config.AddChannel(QosType.Unreliable);

        /*
        Host topology: 
        (1) how many connection with default config will be supported
        (2) what will be special connections (connections with config different from default). 
         
        */

        HostTopology hostTop = new HostTopology(config, MaxConnections);
        hostID = NetworkTransport.AddHost(hostTop, port, null); // ip is left out since this is the server


    }

}
