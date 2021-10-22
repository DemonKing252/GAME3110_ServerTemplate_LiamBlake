using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Networking;

public static class ClientToServerSignifier
{
    public const int Login = 1;
    public const int CreateAccount = 2;

    public const int AddToGameSessionQueue = 3;
    public const int TicTacToePlay = 4;
    public const int UpdateBoard = 5;

}
public static class ServerToClientSignifier
{
    public const int LoginResponse = 101;
    public const int CreateResponse = 102;

    public const int GameSessionStarted = 103;

    public const int OpponentTicTacToePlay = 104;
    public const int UpdateBoardOnClientSide = 105;
    public const int VerifyConnection = 106;


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




// So I can view it from the inspector
[Serializable]
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

[System.Serializable]
public class Client
{
    public int connectionId;
    public string username;
    public bool loggedin = false;
}

[System.Serializable]
public class BoardView
{
    public string[] slots = new string[9]
    {
        "",
        "",
        "",
        "",
        "",
        "",
        "",
        "",
        "",
    };
    public BoardView()
    {
        
    }
}
[System.Serializable]
public class GameSession
{
    public char playerTurn;
    public int playerId1;
    public int playerId2;
    public BoardView board = new BoardView();

    public GameSession(int id1, int id2)
    {
        // randomize who goes first
        int turn = UnityEngine.Random.Range(1, 3);
        if (turn == 1)
        {
            playerTurn = 'X';
        }
        else
        {
            playerTurn = 'O';
        }


        playerId1 = id1;
        playerId2 = id2;
    }

}


public class NetworkedServer : MonoBehaviour
{
    [SerializeField]
    private List<Client> clients = new List<Client>();

    [SerializeField]
    private Client player1 = null;

    [SerializeField]
    private Client player2 = null;



    [SerializeField]
    private List<GameSession> sessions = new List<GameSession>();

    int maxConnections = 1000;
    int reliableChannelID;
    int unreliableChannelID;
    int hostID;
    int socketPort = 5491;

    private string path;

    [SerializeField]
    private LinkedList<PlayerAccount> playerAccounts = new LinkedList<PlayerAccount>();
    private int playerwaitingformatch = -1;

    // Start is called before the first frame update
    void Start()
    {
        path = Application.dataPath + Path.DirectorySeparatorChar + "Resources" + Path.DirectorySeparatorChar + "PlayerAccounts.txt";
        LoadAccounts();
        
        NetworkTransport.Init();
        ConnectionConfig config = new ConnectionConfig();
        reliableChannelID = config.AddChannel(QosType.Reliable);
        unreliableChannelID = config.AddChannel(QosType.Unreliable);
        HostTopology topology = new HostTopology(config, maxConnections);
        hostID = NetworkTransport.AddHost(topology, socketPort, null);



    }
    void OnApplicationQuit()
    {
        SaveAccounts();
    }

    public void SaveAccounts()
    {
        StreamWriter sw = new StreamWriter(path);

        try
        {
            // num of accounts|account user, account password|next account . . . . .

            string data = playerAccounts.Count.ToString() + "|";

            foreach (PlayerAccount p in playerAccounts)
            {
                data += p.username + "," + p.password + "|";
            }
            sw.WriteLine(data);
            sw.Close();
            
        }
        catch(Exception e)
        {
            Debug.LogError("ERROR when saving: " + e.Message);
        }
    }

    public void LoadAccounts()
    {

        try
        {
            if (File.Exists(path))
            {
                StreamReader sr = new StreamReader(path);

                string rawdata;
                
                rawdata = sr.ReadLine();

                if (rawdata == null)
                {
                    rawdata = "0|";
                }

                // seperated data
                string[] sData = rawdata.Split('|');

                int numAccounts = int.Parse(sData[0]);

                int index = 1;
                for (int i = 0; i < numAccounts; i++)
                {
                    string[] accountinfo = sData[index].Split(',');
                    PlayerAccount p = new PlayerAccount(accountinfo[0], accountinfo[1]);
                    playerAccounts.AddLast(p);

                    index++;
                }

                //Debug.Log("num accounts: " + numAccounts.ToString());
            }
            else
            {
                // Create a file for writing
                SaveAccounts();

                // Recursion comes in handy here
                LoadAccounts();
            }
        }
        catch (Exception e)
        {
            Debug.LogError("ERROR when loading: " + e.Message);
        }
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

                OnClientConnected(recConnectionID);

                break;
            case NetworkEventType.DataEvent:
                string msg = Encoding.Unicode.GetString(recBuffer, 0, dataSize);
                ProcessRecievedMsg(msg, recConnectionID);
                break;
            case NetworkEventType.DisconnectEvent:
                OnClientDisconnected(recConnectionID);

                break;
        }

    }

    public void SendMessageToClient(string msg, int id)
    {
        byte error = 0;
        byte[] buffer = Encoding.Unicode.GetBytes(msg);
        NetworkTransport.Send(hostID, id, reliableChannelID, buffer, msg.Length * sizeof(char), out error);
    }
    private void OnClientConnected(int recConnectionId)
    {
        Debug.Log("Connection, " + recConnectionId);

        Client temp = new Client();
        temp.connectionId = recConnectionId;
        clients.Add(temp);
    
        if (clients.Count == 1)
        {
            player1 = temp;
        }
        else if (clients.Count == 2)
        {
            player2 = temp;
        }
        else if (clients.Count >= 3)
        {
            // handle observers here...
            
        }

        string _msg = ServerToClientSignifier.VerifyConnection.ToString() + ",";
        SendMessageToClient(_msg, recConnectionId);

    }
    private void OnClientDisconnected(int recConnectionId)
    {
        Debug.Log("Disconnection, " + recConnectionId);

        foreach (Client c in clients)
        {
            if (c.connectionId == recConnectionId)
            {
                clients.Remove(c);
                break;
            }
        }
    }

    private void ProcessRecievedMsg(string msg, int id)
    {
        Debug.Log("msg recieved = " + msg + ".  connection id = " + id);

        string[] data = msg.Split(',');

        int signifier = int.Parse(data[0]);


        if (signifier == ClientToServerSignifier.CreateAccount)
        {
            string _user = data[1];
            string _pass = data[2];

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
            string _user = data[1];
            string _pass = data[2];
            //                      why is this true?
            // assume that by default, if a user is not found, it cleary doesn't exist
            // this loop only checks if its the WRONG user, not if it DOESNT exist
            // think about that for a second

            bool wasFound = false;
            foreach (PlayerAccount p in playerAccounts)
            {
                // assume it exists unless otherwise it doesnt
                //wronguser = true;
                if (p.username == _user)
                {
                    if (p.password == _pass)
                    {
                        foreach(Client c in clients)
                        {
                            if (c.connectionId == id)
                            {
                                c.username = p.username;
                                c.loggedin = true;
                            }
                        }
                        // Successful
                        SendMessageToClient(ServerToClientSignifier.LoginResponse.ToString() + "," + LoginResponse.Success.ToString(), id);
                        
                    }
                    else
                    {
                        // Correct username but wrong password
                        SendMessageToClient(ServerToClientSignifier.LoginResponse.ToString() + "," + LoginResponse.WrongPassword.ToString(), id);
                    }

                    wasFound = true;
                    break;
                }
            }

            // If the user name was found
            if (!wasFound)
            {

                SendMessageToClient(ServerToClientSignifier.LoginResponse.ToString() + "," + LoginResponse.WrongName.ToString(), id);
            }
        }
        else if (signifier == ClientToServerSignifier.AddToGameSessionQueue)
        {
            // first player waiting
            if (playerwaitingformatch == -1)
            {
                playerwaitingformatch = id;            
            }
            // player is waiting, and a new client has joined, so we can create the game session.
            else
            {
                GameSession gs = new GameSession(playerwaitingformatch, id);

                sessions.Add(gs);

                SendMessageToClient(ServerToClientSignifier.GameSessionStarted.ToString() + "," + sessions.Count.ToString() + ",X," + gs.playerTurn, id);
                SendMessageToClient(ServerToClientSignifier.GameSessionStarted.ToString() + "," + sessions.Count.ToString() + ",O," + gs.playerTurn, playerwaitingformatch);

                playerwaitingformatch = -1;

                // there is a waiting player, join
            }

        }
        else if (signifier == ClientToServerSignifier.TicTacToePlay)
        {
            GameSession gs = FindGameSessionWithPlayerID(id);
            
            if (gs != null)
            {
                SendMessageToClient(ServerToClientSignifier.OpponentTicTacToePlay.ToString() + "," + "hello from server", gs.playerId1);
                SendMessageToClient(ServerToClientSignifier.OpponentTicTacToePlay.ToString() + "," + "hello from server", gs.playerId2);

            }

            //if (gs.playerId1 == id)
            //{
            //    SendMessageToClient(ServerToClientSignifier.OpponentTicTacToePlay.ToString() + ",", gs.playerId1);
            //}
            //else
            //{
            //    SendMessageToClient(ServerToClientSignifier.OpponentTicTacToePlay.ToString() + ",", gs.playerId2);
            //}
        }
        else if (signifier == ClientToServerSignifier.UpdateBoard)
        {
            Debug.Log("Updating board . . .");

            GameSession gs = FindGameSessionWithPlayerID(id);
            Assert.IsNotNull(gs, "Error: game session was null!");

            try
            {
                // Update board UI here.
                int index = 1;
                for (int i = 0; i < 9; i++)
                {
                    gs.board.slots[i] = data[index];
                    index++;
                }

                gs.playerTurn = (gs.playerTurn == 'X' ? 'O' : 'X');

                // Inform the new board changes, and update who should have authority to go next
                string _msg = ServerToClientSignifier.UpdateBoardOnClientSide.ToString() + "," + gs.playerTurn.ToString() + ",";

                foreach(string s in gs.board.slots)
                {
                    _msg += s + ",";

                }
                SendMessageToClient(_msg, gs.playerId1);
                SendMessageToClient(_msg, gs.playerId2);

            }
            catch(Exception e)
            {
                Debug.LogError("Error when updating board: " + e.Message);
            }
        }
    }
    private GameSession FindGameSessionWithPlayerID(int id)
    {
        foreach(GameSession gs in sessions)
        {
            if (gs.playerId1 == id || gs.playerId2 == id)
            {
                return gs;
            }
        }
        return null;
    }
}
