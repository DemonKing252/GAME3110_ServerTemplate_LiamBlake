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

    public const int ChatMessage = 6;
    public const int AddToObserverSessionQueue = 7;
    public const int LeaveSession = 8;
    public const int LeaveServer = 9;

}
public static class ServerToClientSignifier
{
    public const int LoginResponse = 101;
    public const int CreateResponse = 102;

    public const int GameSessionStarted = 103;

    public const int OpponentTicTacToePlay = 104;
    public const int UpdateBoardOnClientSide = 105;
    public const int VerifyConnection = 106;

    public const int MessageToClient = 107;
    public const int UpdateSessions = 108;

    public const int ConfirmObserver = 109;

    public const int PlayerDisconnected = 110;

}
// manage sending our chat message to clients who we want to have authority 
public static class MessageAuthority
{
    // These responses can be xxx digits, because they wont be checked anywhere else unless under the 
    // condition of "ChatMessage" (signafier = 6)
    // just to make sure though, im leaving a space of 50 between them.

    public const int ToGameSession = 151;       // To clients in the game session
    public const int ToObservers = 152;         // To observer clients
    public const int ToOtherClients = 153;      // To game session clients
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
    public List<int> observerIds = new List<int>();

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
        catch (Exception e)
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


        NotifyClientsAboutSessionUpdate();
        //if (clients.Count == 1)
        //{
        //    player1 = temp;
        //}
        //else if (clients.Count == 2)
        //{
        //    player2 = temp;
        //}
        //else if (clients.Count >= 3)
        //{
        //    // handle observers here...

        //}

        string _msg = ServerToClientSignifier.VerifyConnection.ToString() + ",";
        SendMessageToClient(_msg, recConnectionId);

    }
    private void OnClientDisconnected(int recConnectionId)
    {
        Debug.Log("Disconnection, " + recConnectionId);
        //
        //GameSession gs = FindGameSessionWithPlayerID(recConnectionId);
        //
        //// If a client leaves, the session gets destroyed because the game cant continue with 1 client (observers dont matter)
        //// Because of this, the game session is now null, and if the second client wants to quit, this will throw
        //// an exception because the client id in game session no longer exists.
        //// Thats why we need this.
        //
        //if (gs != null)
        //{
        //    string _disconnectMsg = ServerToClientSignifier.PlayerDisconnected + ",";   // We always need the comma, or else were going to read garbage data which will cause a lot of problems
        //
        //    if (gs.playerId1 != recConnectionId)// we dont want to send it to the same person that quit
        //        SendMessageToClient(_disconnectMsg, gs.playerId1);
        //
        //    if (gs.playerId2 != recConnectionId)// we dont want to send it to the same person that quit
        //        SendMessageToClient(_disconnectMsg, gs.playerId2);
        //
        //    foreach (int observerId in gs.observerIds)
        //        if (observerId != recConnectionId)// we dont want to send it to the same person that quit
        //            SendMessageToClient(_disconnectMsg, observerId);
        //
        //    sessions.Remove(gs);
        //
        //}
        //RemoveClientAt(recConnectionId);
        //
        //// Refresh the sessions available on client side
        //NotifyClientsAboutSessionUpdate();


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
            foreach (PlayerAccount p in playerAccounts)
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
                        foreach (Client c in clients)
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

                // We have to nofify all clients.
                // Theres no point in waiting for a client to start observing before we notify them about the 
                // sessions in queue.
                NotifyClientsAboutSessionUpdate();


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


            // Doing a try-catch handler because array access by iteration can be flaky if not done properly,
            // so decrypting this error will be alot easier!
            try
            {

                // ------------------------------------------
                // Notify all clients and observers

                UpdateBoardUI(gs, data);
                string _msg = GetParsedBoardData(gs);

                SendMessageToClient(_msg, gs.playerId1);
                SendMessageToClient(_msg, gs.playerId2);

                foreach (int observerId in gs.observerIds)
                {
                    SendMessageToClient(_msg, observerId);
                }

            }
            catch (Exception e)
            {
                Debug.LogError("Error when updating board: " + e.Message);
                Assert.IsNotNull(gs, "Error: game session was null!");
            }
        }
        else if (signifier == ClientToServerSignifier.ChatMessage)
        {
            GameSession gs = FindGameSessionWithPlayerID(id);

            Assert.IsNotNull(gs, "Error, game session was null when recieving chat message");
            // send to game session


            try
            {

                bool _togamesession = bool.Parse(data[1]);
                bool _toobservers = bool.Parse(data[2]);
                bool _to_otherclients = bool.Parse(data[3]);

                string _msg = ServerToClientSignifier.MessageToClient + "," + data[4];

                if (_togamesession)
                {
                    SendMessageToClient(_msg, gs.playerId1);
                    SendMessageToClient(_msg, gs.playerId2);
                }
                if (_toobservers)
                {
                    // handle sending to observers once we implement it . . .
                    foreach (int observerId in gs.observerIds)
                    {
                        SendMessageToClient(_msg, observerId);
                    }
                }
                if (_to_otherclients)
                {
                    foreach (GameSession g in sessions)
                    {
                        // we dont want to send a copy of our message to the same session, we already determined that
                        // if we dont have this, then our current session will recieve a copy of the message twice.

                        if (g.playerId1 != id && g.playerId2 != id && !ObserverExistsInThisSession(gs, id))
                        {

                            SendMessageToClient(_msg, g.playerId1);
                            SendMessageToClient(_msg, g.playerId2);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("Error when parsing authority options: " + e.Message);
            }




        }
        else if (signifier == ClientToServerSignifier.AddToObserverSessionQueue)
        {
            int sessionIndex = int.Parse(data[1]);
            Assert.IsTrue(sessionIndex <= sessions.Count - 1, "Error: Out of bounds except when adding observer to session queue!");

            sessions[sessionIndex].observerIds.Add(id);

            string _msg = GetParsedBoardData(sessions[sessionIndex]);

            // This should not throw, otherwise we have a logic error, so im adding this in:
            SendMessageToClient(ServerToClientSignifier.ConfirmObserver + ",", id);
            SendMessageToClient(_msg, id);
        }
        else if (signifier == ClientToServerSignifier.LeaveSession)
        {
            GameSession gs = FindGameSessionWithPlayerID(id);

            // If a client leaves, the session gets destroyed because the game cant continue with 1 client (observers dont matter)
            // Because of this, the game session is now null, and if the second client wants to quit, this will throw
            // an exception because the client id in game session no longer exists.
            // Thats why we need this.

            bool IsObserver = bool.Parse(data[1]);

            // If were a player, we end the session, because the game is over
            // General rule is you cant let someone else take over the game session
            // Because that makes for an unfair game
            
            if (gs != null && !IsObserver)
            {
                string _disconnectMsg = ServerToClientSignifier.PlayerDisconnected + ",";   // We always need the comma, or else were going to read garbage data which will cause a lot of problems

                if (gs.playerId1 != id)// we dont want to send it to the same person that quit
                    SendMessageToClient(_disconnectMsg, gs.playerId1);

                if (gs.playerId2 != id)// we dont want to send it to the same person that quit
                    SendMessageToClient(_disconnectMsg, gs.playerId2);

                foreach (int observerId in gs.observerIds)
                    if (observerId != id)// we dont want to send it to the same person that quit
                        SendMessageToClient(_disconnectMsg, observerId);

                sessions.Remove(gs);

            }
            // If the player leaving is an observer, remove there id from the list, but the session continues as normal
            if (IsObserver && gs != null)
            {
                foreach(int observerId in gs.observerIds)
                {
                    if (observerId == id)
                    {
                        gs.observerIds.Remove(observerId);
                        break;
                    }
                }
            }


            // Refresh the sessions available on client side
            NotifyClientsAboutSessionUpdate();

        }
        else if (signifier == ClientToServerSignifier.LeaveServer)
        {
            GameSession gs = FindGameSessionWithPlayerID(id);

            // If a client leaves, the session gets destroyed because the game cant continue with 1 client (observers dont matter)
            // Because of this, the game session is now null, and if the second client wants to quit, this will throw
            // an exception because the client id in game session no longer exists.
            // Thats why we need this.

            bool IsObserver = bool.Parse(data[1]);

            // If were a player, we end the session, because the game is over
            // General rule is you cant let someone else take over the game session
            // Because that makes for an unfair game

            if (gs != null && !IsObserver)
            {
                string _disconnectMsg = ServerToClientSignifier.PlayerDisconnected + ",";   // We always need the comma, or else were going to read garbage data which will cause a lot of problems

                if (gs.playerId1 != id)// we dont want to send it to the same person that quit
                    SendMessageToClient(_disconnectMsg, gs.playerId1);

                if (gs.playerId2 != id)// we dont want to send it to the same person that quit
                    SendMessageToClient(_disconnectMsg, gs.playerId2);

                foreach (int observerId in gs.observerIds)
                    if (observerId != id)// we dont want to send it to the same person that quit
                        SendMessageToClient(_disconnectMsg, observerId);

                sessions.Remove(gs);

            }
            // If the player leaving is an observer, remove there id from the list, but the session continues as normal
            if (IsObserver && gs != null)
            {
                foreach (int observerId in gs.observerIds)
                {
                    if (observerId == id)
                    {
                        gs.observerIds.Remove(observerId);
                        break;
                    }
                }
            }

            // Remove the client from our list
            RemoveClientAt(id);

            // Refresh the sessions available on client side
            NotifyClientsAboutSessionUpdate();
        }
    }

    public bool ObserverExistsInThisSession(GameSession gs, int id)
    {
        foreach (int i in gs.observerIds)
        {
            if (i == id)
                return true;
        }
        return false;
    }
    public void RemoveClientAt(int id)
    {
        foreach (Client c in clients)
        {
            if (c.connectionId == id)
            {
                clients.Remove(c);
                break;
            }
        }
    }

    public void NotifyClientsAboutSessionUpdate()
    {
        string _msg = ServerToClientSignifier.UpdateSessions + "," + sessions.Count + ",";
        for (int i = 0; i < sessions.Count; i++)
        {
            _msg += i.ToString() + ",";
        }

        foreach (Client c in clients)
        {
            SendMessageToClient(_msg, c.connectionId);
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
            foreach(int observerId in gs.observerIds)
            {
                if (observerId == id)
                    return gs;
            }
        }
        return null;
    }
    public void UpdateBoardUI(GameSession gs, string[] data)
    {
        // Update board UI here.
        int index = 1;
        for (int i = 0; i < 9; i++)
        {
            gs.board.slots[i] = data[index];
            index++;
        }

        gs.playerTurn = (gs.playerTurn == 'X' ? 'O' : 'X');

        
    }
    public string GetParsedBoardData(GameSession gs)
    {
        // Inform the new board changes, and update who should have authority to go next
        string _msg = ServerToClientSignifier.UpdateBoardOnClientSide.ToString() + "," + gs.playerTurn.ToString() + ",";

        foreach (string s in gs.board.slots)
        {
            _msg += s + ",";

        }
        return _msg;
    }

}
