using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;
using UnityEngine.UI;

public static class ClientToServerSignifier
{
    public const int Login = 1;
    public const int CreateAccount = 2;

}
public static class ServerToClientSignifier
{
    public const int LoginResponse = 101;
    public const int CreateResponse = 102;

}

public static class LoginResponse
{
    public const int Success = 1001;

    public const int WrongNameAndPassword = 1002;
    public const int WrongName = 1003;
    public const int WrongPassword = 1004;
}
public static class CreateResponse
{
    // 10,000
    public const int Success = 10001;
    public const int UsernameTaken = 10002;
}



public class PlayerAccount
{
    public string username;
    public string password;

    public PlayerAccount(string user, string pass)
    {
        this.username = user;
        this.password = pass;
    }

}

public class NetworkedServer : MonoBehaviour
{
    int maxConnections = 1000;
    int reliableChannelID;
    int unreliableChannelID;
    int hostID;
    int socketPort = 5491;

    private LinkedList<PlayerAccount> playerAccounts = new LinkedList<PlayerAccount>();

    // Start is called before the first frame update
    void Start()
    {
        NetworkTransport.Init();
        ConnectionConfig config = new ConnectionConfig();
        reliableChannelID = config.AddChannel(QosType.Reliable);
        unreliableChannelID = config.AddChannel(QosType.Unreliable);
        HostTopology topology = new HostTopology(config, maxConnections);
        hostID = NetworkTransport.AddHost(topology, socketPort, null);

    }

    // Update is called once per frame
    void Update()
    {

        int recHostID;
        int recConnectionID;
        int recChannelID;
        byte[] recBuffer = new byte[1024];
        int bufferSize = 1024;
        int dataSize;
        byte error = 0;

        NetworkEventType recNetworkEvent = NetworkTransport.Receive(out recHostID, out recConnectionID, out recChannelID, recBuffer, bufferSize, out dataSize, out error);

        switch (recNetworkEvent)
        {
            case NetworkEventType.Nothing:
                break;
            case NetworkEventType.ConnectEvent:
                Debug.Log("Connection, " + recConnectionID);
                break;
            case NetworkEventType.DataEvent:
                string msg = Encoding.Unicode.GetString(recBuffer, 0, dataSize);
                ProcessRecievedMsg(msg, recConnectionID);
                break;
            case NetworkEventType.DisconnectEvent:
                Debug.Log("Disconnection, " + recConnectionID);
                break;
        }

    }

    public void SendMessageToClient(string msg, int id)
    {
        byte error = 0;
        byte[] buffer = Encoding.Unicode.GetBytes(msg);
        NetworkTransport.Send(hostID, id, reliableChannelID, buffer, msg.Length * sizeof(char), out error);
    }

    private void ProcessRecievedMsg(string msg, int id)
    {
        Debug.Log("msg recieved = " + msg + ".  connection id = " + id);

        string[] data = msg.Split(',');

        int signifier = int.Parse(data[0]);

        string _user = data[1];
        string _pass = data[2];

        if (signifier == ClientToServerSignifier.CreateAccount)
        {

            bool wasFound = false;
            foreach(PlayerAccount p in playerAccounts)
            {
                if (p.username == _user)
                {
                    wasFound = true;
                    break;
                }
            }
            if (!wasFound)
            {
                playerAccounts.AddLast(new PlayerAccount(_user, _pass));
                SendMessageToClient(ServerToClientSignifier.CreateResponse.ToString() + "," + CreateResponse.Success.ToString(), id);

                //SendMessageToClient(ServerToClientSignifier.LoginResponse.ToString() + "," + LoginResponse.Success.ToString(), 0);
            }
            else
            {
                SendMessageToClient(ServerToClientSignifier.CreateResponse.ToString() + "," + CreateResponse.UsernameTaken.ToString(), id);

            }
        }
        else if (signifier == ClientToServerSignifier.Login)
        {
            //                      why is this true?
            // assume that by default, if a user is not found, it cleary doesn't exist
            // this loop only checks if its the WRONG user, not if it DOESNT exist
            // think about that for a second

            bool wrongpass = false, wronguser = true;
            bool wasFound = false;
            foreach (PlayerAccount p in playerAccounts)
            {
                // assume it exists unless otherwise it doesnt
                wronguser = false;
                if (p.username == _user && p.password == _pass)
                {
                    wasFound = true;
                    break;
                }
                else
                {
                    // If password is wrong
                    if (p.username == _user && p.password != _pass)
                        wrongpass = true;
                    // If user is wrong
                    else if (p.username != _user && (p.password == _pass || p.password != _pass))
                        wronguser = true;

                }
            }

            // If the user name was found
            if (wasFound)
            {

                SendMessageToClient(ServerToClientSignifier.LoginResponse.ToString() + "," + LoginResponse.Success.ToString(), id);
                //playerAccounts.AddLast(new PlayerAccount(_user, _pass));
            }
            else
            {
                if (wronguser)
                    SendMessageToClient(ServerToClientSignifier.LoginResponse.ToString() + "," + LoginResponse.WrongName.ToString(), id);
                else if (wrongpass)
                    SendMessageToClient(ServerToClientSignifier.LoginResponse.ToString() + "," + LoginResponse.WrongPassword.ToString(), id);
            }
        }

    }
}
