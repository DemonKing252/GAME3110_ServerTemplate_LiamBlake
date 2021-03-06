using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Assertions;
using UnityEngine.Networking;

// Client to server message
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

    public const int SendRecord = 10;
    public const int RecordSendingDone = 11;

}
// Server to client message
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

    public const int SendRecording = 111;
    public const int QueueEndOfRecord = 112;
    public const int QueueStartOfRecordings = 113;

    public const int QueueEndOfRecordings = 114;

    public const int KickPlayer = 115;
    public const int ConnectionLost = 116;

}
// The result of a client attempting to login (this is a server to client signifier)
public static class LoginResponse
{
    public const int Success = 1001;

    public const int WrongNameAndPassword = 1002;
    public const int WrongName = 1003;
    public const int WrongPassword = 1004;

    public const int AccountAlreadyUsedByAnotherPlayer = 1005;
    public const int AccountBanned = 1006;
}
// The result of a client attempting to create an account (this is a server to client signifier)
public static class CreateResponse
{
    // 10,000
    public const int Success = 10001;
    public const int UsernameTaken = 10002;
}




// So I can view it from the inspector
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

public class Client
{
    public int connectionId;
    public string username;
    public bool loggedin = false;
}

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
        // Nothing needed in here, unless we want to scale on this board view
        // usually every board slot starts out as empty.
    }
}
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
public class Record
{
    public float timeRecorded = 0f;
    public char[] slots = new char[9]
    {
        ' ',
        ' ',
        ' ',
        ' ',
        ' ',
        ' ',
        ' ',
        ' ',
        ' ',
    };
    public Record()
    {
        messages = new List<string>();
    }

    public List<string> messages;
    public string serverResponse;

    public string SerializeData()
    {
        string temp = "";
        foreach (char s in slots)
        {
            temp += s.ToString() + "|";
        }
        temp += serverResponse + "|" + timeRecorded.ToString("F2") + "|+";
        foreach (string m in messages)
        {
            temp += m + "|";
        }

        return temp;
    }
    public void DeserializeData(string[] gameData)
    {

        string[] boardData = gameData[0].Split('|');

        // Using index 0 will allow you to get the character in the string (index 0)
        for (int i = 0; i < slots.Length; i++)
            slots[i] = boardData[i][0];
        

        // Server response status (the text on screen above the board)
        serverResponse = boardData[9];
        timeRecorded = float.Parse(boardData[10]);

        string[] textData = gameData[1].Split('|');
        foreach (string s in textData)
        {
            messages.Add(s);
        }
    }
}

public class Recording
{
    // Theres no point in parsing the time recorded back to its original System.Time, if were going to 
    // to just send it back to the client as a CSV anyway

    public string username;   // username that this was recorded from
    public string timeRecorded;
    public List<Record> records = new List<Record>();
}
// Better to keep them as constants
public static class ServerCommand
{
    public const string Help = "help";
    public const string KickPlayer = "kick";
    public const string ClearConsole = "clear";
    public const string StopServer = "stop";
    public const string BanPlayer = "ban";
    public const string UnBanPlayer = "unban";

}


public class NetworkedServer : MonoBehaviour
{
    // This list is for reading sub divided record data.
    private List<Record> clientRecords = new List<Record>();

    // This list is for the actual records that have been recorded.

    private List<Recording> recordings = new List<Recording>();

    private List<Client> clients = new List<Client>();

    private List<GameSession> sessions = new List<GameSession>();

    [SerializeField]
    private GameObject textPrefab;

    [SerializeField] 
    private GameObject serverLogParent;

    [SerializeField]
    private InputField cmdField;

    int maxConnections = 1000;
    int reliableChannelID;
    int unreliableChannelID;
    int hostID;
    int socketPort = 5491;

    private string playerAccountPath;
    private string bannedPlayersPath;
    private string recordingsPath;

    [SerializeField]
    private LinkedList<PlayerAccount> playerAccounts = new LinkedList<PlayerAccount>();
    private int playerwaitingformatch = -1;

    private List<string> bannedPlayers = new List<string>();

    // A list of all active accounts in the server
    // This is used to prevent more than one user from entering with the same account.
    private List<PlayerAccount> activeAccounts = new List<PlayerAccount>();

    private const int recordSize = 224;
    private int MaxElementsPerRecord;

    // Command start
    public const string commandSignifier = "/";
    
    // If the command needs to know a username, this is the signifier that its looking for
    public const string usernameCommandSignifier = " ";

    // Start is called before the first frame update
    void Start()
    {
        // Maximum number of elements we can specify in one packet
        MaxElementsPerRecord = Mathf.FloorToInt((float)1024 / (float)recordSize);

        playerAccountPath = Application.dataPath + Path.DirectorySeparatorChar + "Resources" + Path.DirectorySeparatorChar + "PlayerAccounts.txt";
        bannedPlayersPath = Application.dataPath + Path.DirectorySeparatorChar + "Resources" + Path.DirectorySeparatorChar + "BannedPlayers.txt";
        recordingsPath = Application.dataPath + Path.DirectorySeparatorChar + "Resources" + Path.DirectorySeparatorChar + "Recorings.txt";


        NewServerMessage("Loading player accounts..");
        LoadAccounts();

        NewServerMessage("Loading banned players..");
        LoadBannedPlayers();

        NewServerMessage("Loading recordings..");
        LoadRecordings();

        NetworkTransport.Init();
        ConnectionConfig config = new ConnectionConfig();
        reliableChannelID = config.AddChannel(QosType.Reliable);
        unreliableChannelID = config.AddChannel(QosType.Unreliable);
        HostTopology topology = new HostTopology(config, maxConnections);
        hostID = NetworkTransport.AddHost(topology, socketPort, null);

        NewServerMessage("Server started.");


    }

    public bool DoesCommandExist(string cmd)
    {
        return
        (
            cmd == ServerCommand.Help ||
            cmd == ServerCommand.ClearConsole ||
            cmd == ServerCommand.KickPlayer ||
            cmd == ServerCommand.StopServer || 
            cmd == ServerCommand.BanPlayer || 
            cmd == ServerCommand.UnBanPlayer
        );
    }

    public void OnCommandEntered(string cmd)
    {
        string[] cmdData = cmd.Split('/');

        if (cmdData.Length < 2 && cmd.Length > 0)
        {
            NewServerWarning("Unknown command signifier! Commands start with '" + commandSignifier + "'");
            cmdField.text = string.Empty;
        }
        else if (cmd.Length > 0)    // Left clicking on a input field registers an end input so we dont want to process the command if the string is empty.
        {
            string[] cmdSubAttributes = cmdData[1].Split(' ');

            if (DoesCommandExist(cmdSubAttributes[0]))
            {
                // Verify that the user entered the footer command properly
                if (cmdSubAttributes.Length == 2)
                {
                    // If this command has a footer (ie: kick username, ban username etc)
                    ProcessCommand(cmdSubAttributes[0], cmdSubAttributes[1]);
                }
                else
                {
                    // Else its just a simple keyword command
                    ProcessCommand(cmdData[1], string.Empty);
                }
                cmdField.text = string.Empty;
            }
            else
            {
                NewServerWarning("Unknown command!");
                cmdField.text = string.Empty;
            }
        }
        
    }
    public void PostListOfCommands()
    {
        NewServerMessageWithCustomColor(commandSignifier + ServerCommand.ClearConsole + " clear the console", Color.cyan);
        NewServerMessageWithCustomColor(commandSignifier + ServerCommand.KickPlayer + " 'username' to kick a player (note: kicking a player AUTOMATICALLY adds them to the banned players list!)", Color.cyan);
        NewServerMessageWithCustomColor(commandSignifier + ServerCommand.StopServer + " to stop the server", Color.cyan);
        NewServerMessageWithCustomColor(commandSignifier + ServerCommand.BanPlayer + " 'username' to ban a player", Color.cyan);
        NewServerMessageWithCustomColor(commandSignifier + ServerCommand.UnBanPlayer + " 'username' to unban a player", Color.cyan);

    }
    public void ProcessCommand(string header, string footer)
    {
        switch (header)
        {
            case ServerCommand.Help:
                PostListOfCommands();
                break;
            case ServerCommand.StopServer:
                StopServer();
                break;
            case ServerCommand.KickPlayer:
                // Kick player from session
                int id = GetIdFromUserName(footer);
                
                if (id != -1)
                {
                    SendMessageToClient(ServerToClientSignifier.KickPlayer + ",", id); 
                    NewServerMessageWithCustomColor("Kicked " + footer + " from the game.", new Color(1.0f, 0.65f, 0.0f));
                    RemoveUsernameWithID(id);

                    // ban player
                    bannedPlayers.Add(footer);
                }
                else
                {
                    NewServerMessageWithCustomColor("That username does not exist!.", Color.red);
                }
                break;
            case ServerCommand.BanPlayer:
                bool exists = false;
                foreach(string bP in bannedPlayers)
                {
                    if (bP == footer)
                    {
                        exists = true;
                        break;
                    }
                }

                if (exists)
                {
                    NewServerMessageWithCustomColor("That username is already banned.", new Color(1.0f, 0.65f, 0.0f));
                }
                else
                {
                    id = GetIdFromUserName(footer);

                    if (id != -1)
                    {
                        SendMessageToClient(ServerToClientSignifier.KickPlayer + ",", id);
                        NewServerMessageWithCustomColor("Kicked " + footer + " from the game.", new Color(1.0f, 0.65f, 0.0f));
                        RemoveUsernameWithID(id);

                        // ban player
                        bannedPlayers.Add(footer);
                    }
                    else
                    {
                        NewServerMessageWithCustomColor("That username does not exist!.", Color.red);
                    }

                    bannedPlayers.Add(footer);
                    NewServerMessageWithCustomColor("Banned " + footer + " from the server.", new Color(1.0f, 0.65f, 0.0f));
                }

                // Kick player if hes in the session

                break;
            case ServerCommand.UnBanPlayer:
                if (bannedPlayers.Remove(footer))
                {
                    NewServerMessageWithCustomColor("Unbanned " + footer + " from the server.", new Color(1.0f, 0.65f, 0.0f));
                }
                else 
                {
                    NewServerMessageWithCustomColor("That username does not exist!", Color.red);
                }

                break;
            case ServerCommand.ClearConsole:

                ClearConsole();
                break;
        
        }

    }

    public void ClearConsole()
    {
        GameObject[] _gos = GameObject.FindGameObjectsWithTag("ServerMessage");
        foreach(GameObject go in _gos)
        {
            Destroy(go);
        }
    }

    private void StopServer()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
    public void NewServerMessageWithCustomColor(string text, Color c)
    {
        GameObject go = Instantiate(textPrefab, serverLogParent.transform);
        go.GetComponent<Text>().text = GetFormattedTime() + text;
        go.GetComponent<Text>().color = c;
        go.tag = "ServerMessage";
    }

    public void NewServerMessage(string text)
    {
        GameObject go = Instantiate(textPrefab, serverLogParent.transform);
        go.GetComponent<Text>().text = GetFormattedTime() + text;
        go.GetComponent<Text>().color = Color.green;
        go.tag = "ServerMessage";
    }
    public void NewServerWarning(string text)
    {
        GameObject go = Instantiate(textPrefab, serverLogParent.transform);
        go.GetComponent<Text>().text = GetFormattedTime() + text;
        go.GetComponent<Text>().color = Color.yellow;
        go.tag = "ServerMessage";
    }
    public void NewServerError(string text)
    {
        GameObject go = Instantiate(textPrefab, serverLogParent.transform);
        go.GetComponent<Text>().text = GetFormattedTime() + text;
        go.GetComponent<Text>().color = Color.red;
        go.tag = "ServerMessage";
        
    }
    
    void OnApplicationQuit()
    {
        NewServerMessage("Saving player accounts..");
        SaveAccounts();

        NewServerMessage("Saving banned players");
        SaveBannedPlayers();

        NewServerMessage("Saving recordings");
        SaveRecordings();

        DisconnectAllClients();
    }

    public void SaveAccounts()
    {
        StreamWriter sw = new StreamWriter(playerAccountPath);

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
    public static string GetFormattedTime()
    {

        System.DateTime dateTime = System.DateTime.Now;
        string txt =
            "[" + dateTime.Month.ToString("00") +
            "-" + dateTime.Day.ToString("00") +
            "-" + dateTime.Year.ToString("00") +
            " " + dateTime.Hour.ToString("00") +
            ":" + dateTime.Minute.ToString("00") +
            ":" + dateTime.Second.ToString("00") + "] (Server): ";

        return txt;
    }
    

    public void LoadBannedPlayers()
    {
        StreamReader sr = new StreamReader(bannedPlayersPath);
        try
        {
            if (File.Exists(bannedPlayersPath))
            {

                string rawdata;
                while ((rawdata = sr.ReadLine()) != null)
                {
                    bannedPlayers.Add(rawdata);
                }

            }
            else
            {
                // Create a file for writing
                SaveBannedPlayers();

                // Recursion comes in handy here
                LoadBannedPlayers();
            }
        }
        catch (Exception e)
        {
            NewServerError("ERROR when loading: " + e.Message);
        }
    }
    public void SaveBannedPlayers()
    {
        StreamWriter sw = new StreamWriter(bannedPlayersPath);

        try
        {
            // num of accounts|account user, account password|next account . . . . .

            foreach(string bP in bannedPlayers)
                sw.WriteLine(bP);
            
            sw.Close();

        }
        catch (Exception e)
        {
            NewServerError("ERROR when saving: " + e.Message);
        }
    }
    public void LoadAccounts()
    {
        StreamReader sr = new StreamReader(playerAccountPath);

        try
        {
            if (File.Exists(playerAccountPath))
            {

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
            NewServerError("ERROR when loading: " + e.Message);
        }
    }
    public void SaveRecordings()
    {
        StreamWriter sw = new StreamWriter(recordingsPath);
        try
        {
            
            foreach(Recording rec in recordings)
            {

                string msg = rec.username + "=" + rec.timeRecorded + "=" + rec.records.Count.ToString() + "=|";
                foreach (Record r in rec.records)
                {

                    // Seperate by commas, the slots, messages, board status and time recoreded are seperated by +'s.
                    foreach (char c in r.slots)
                        msg += c + ",";

                    msg += '+';

                    // We dont want to save empty messages, theres no point, it will just take up more memory.
                    foreach (string m in r.messages)
                        if (m != string.Empty)
                            msg += m + ",";

                    msg += '+' + r.serverResponse + "," + r.timeRecorded.ToString("F2") + '+';

                    msg += '|';
                }

                sw.WriteLine(msg);

            }
            sw.Close();
        }
        catch (Exception e)
        {
            NewServerError("ERROR when saving recordings: " + e.Message);
        }
    }
    public void LoadRecordings()
    {
        StreamReader sr = new StreamReader(recordingsPath);
        try
        {

            if (File.Exists(recordingsPath))
            {
                // Load data here.
                string line = string.Empty;
                while ((line = sr.ReadLine()) != null)
                {
                    Recording recording = new Recording();

                    // Seperation --> UserName=TimeRecorded=NumOfRecords|Data for records goes here...
                    string[] recordingAttributes = line.Split('=');
                    recording.username = recordingAttributes[0];
                    recording.timeRecorded = recordingAttributes[1];

                    string[] recordsData = line.Split('|');
                    int numRecords = int.Parse(recordingAttributes[2]);

                    int index = 1;
                    for (int i = 0; i < numRecords; i++)
                    {
                        Record r = new Record();

                        string[] recordAttributes = recordsData[index].Split('+');

                        // Load board data
                        string[] boardData = recordAttributes[0].Split(',');
                        for (int j = 0; j < 9; j++)
                        {
                            r.slots[j] = boardData[j][0];
                        }
                        // Load messages
                        string[] messageData = recordAttributes[1].Split(',');
                        foreach(string m in messageData)
                        {
                            // Don't load up an empty string
                            if (m != string.Empty)
                                r.messages.Add(m);
                            
                        }

                        // Load board status and time recorded
                        string[] boardAttributes = recordAttributes[2].Split(',');
                        r.serverResponse = boardAttributes[0];
                        r.timeRecorded = float.Parse(boardAttributes[1]);
                        r.timeRecorded = float.Parse(boardAttributes[1]);
                        recording.records.Add(r);

                        index++;
                    }
                    recordings.Add(recording);
                }
            }
            else
            {
                SaveRecordings();
                LoadRecordings();
            }

        }
        catch (Exception e)
        {
            NewServerError("ERROR when loading recordings: " + e.Message);
        }
    }

    // Update is called once per frame
    void Update()
    {

        int recHostID;
        int recConnectionID;
        int recChannelID;
        byte[] recBuffer = new byte[8192];
        int bufferSize = 8192;
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
                ProcessRecievedMsg(msg, recConnectionID, dataSize);
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


        NewServerMessage("Player id " + recConnectionId.ToString() + " connected.");

        Client temp = new Client();
        temp.connectionId = recConnectionId;
        clients.Add(temp);

        // Nofify clients about sessions
        NotifyClientsAboutSessionUpdate();

        // Notify clients about recordings
        NotifyClientsAboutNewRecordings();
        

        string _msg = ServerToClientSignifier.VerifyConnection.ToString() + ",";
        SendMessageToClient(_msg, recConnectionId);

    }
    private void OnClientDisconnected(int recConnectionId)
    {
        Debug.Log("Disconnection, " + recConnectionId);

        NewServerMessage("Player id " + recConnectionId.ToString() + " disconnected.");
    }

    private void ProcessRecievedMsg(string msg, int id, int size)
    {

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


                NewServerMessage("Player id " + id.ToString() + "(create account response): created account successfully.");
            }
            else
            {
                SendMessageToClient(ServerToClientSignifier.CreateResponse.ToString() + "," + CreateResponse.UsernameTaken.ToString(), id);

                NewServerMessage("Player id " + id.ToString() + "(create account response): username taken");
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
                        if (!IsUserNameAlreadyLoggedOn(p.username))
                        {
                            bool thisPlayerIsBanned = false;
                            foreach(string bannedPlayer in bannedPlayers)
                            {
                                if (bannedPlayer == p.username)
                                {
                                    thisPlayerIsBanned = true;
                                    break;
                                }
                            }
                            if (thisPlayerIsBanned)
                            {
                                SendMessageToClient(ServerToClientSignifier.LoginResponse.ToString() + "," + LoginResponse.AccountBanned.ToString(), id);
                            }
                            else
                            {
                                activeAccounts.Add(p);
                                // Successful
                                SendMessageToClient(ServerToClientSignifier.LoginResponse.ToString() + "," + LoginResponse.Success.ToString(), id);
                                NewServerMessage("Player id " + id.ToString() + " (login response): logged in as: " + p.username);
                                NewServerMessageWithCustomColor(p.username + " logged in.", Color.magenta);
                            }
                            
                        }
                        else
                        {
                            SendMessageToClient(ServerToClientSignifier.LoginResponse.ToString() + "," + LoginResponse.AccountAlreadyUsedByAnotherPlayer.ToString(), id);
                            NewServerMessage("Player id " + id.ToString() + " (login response): that username is already logged on!");
                        }

                    }
                    else
                    {
                        // Correct username but wrong password
                        SendMessageToClient(ServerToClientSignifier.LoginResponse.ToString() + "," + LoginResponse.WrongPassword.ToString(), id);

                        NewServerMessage("Player id " + id.ToString() + " (login response): wrong password entered.");
                    }

                    wasFound = true;
                    break;
                }
            }

            // If the user name was found
            if (!wasFound)
            {

                NewServerMessage("Player id " + id.ToString() + " (login response): username not found.");
                SendMessageToClient(ServerToClientSignifier.LoginResponse.ToString() + "," + LoginResponse.WrongName.ToString(), id);
            }
        }
        else if (signifier == ClientToServerSignifier.AddToGameSessionQueue)
        {
            // first player waiting
            if (playerwaitingformatch == -1)
            {

                NewServerMessage(GetUserName(id) + ": started a match");
                playerwaitingformatch = id;
            }
            // player is waiting, and a new client has joined, so we can create the game session.
            else
            {
                GameSession gs = new GameSession(playerwaitingformatch, id);

                sessions.Add(gs);

                NewServerMessage(GetUserName(id) + ": was put into a game room.");

                SendMessageToClient(ServerToClientSignifier.GameSessionStarted.ToString() + "," + sessions.Count.ToString() + ",X," + gs.playerTurn + ",1", id);
                SendMessageToClient(ServerToClientSignifier.GameSessionStarted.ToString() + "," + sessions.Count.ToString() + ",O," + gs.playerTurn + ",2", playerwaitingformatch);

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

            NewServerMessage("Player id " + id.ToString() + " game started");
            GameSession gs = FindGameSessionWithPlayerID(id);

            if (gs != null)
            {
                SendMessageToClient(ServerToClientSignifier.OpponentTicTacToePlay.ToString() + "," + "hello from server", gs.playerId1);
                SendMessageToClient(ServerToClientSignifier.OpponentTicTacToePlay.ToString() + "," + "hello from server", gs.playerId2);

            }

        }
        else if (signifier == ClientToServerSignifier.UpdateBoard)
        {

            GameSession gs = FindGameSessionWithPlayerID(id);
            Assert.IsNotNull(gs, "Error: game session was null!");


            // Doing a try-catch handler because array access by iteration can be flaky if not done properly,
            // so decrypting this error will be alot easier!
            try
            {


                NewServerMessage(GetUserName(id) + ": made a board move");

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
                NewServerError("Error when updating board: " + e.Message);
                Assert.IsNotNull(gs, "Error: game session was null!");
            }
        }
        else if (signifier == ClientToServerSignifier.ChatMessage)
        {
            GameSession gs = FindGameSessionWithPlayerID(id);

            Assert.IsNotNull(gs, "Error, game session was null when recieving chat message");
            // send to game session

            NewServerMessage(GetUserName(id) + ": new message");

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
                NewServerError("Error when parsing authority options: " + e.Message);
            }




        }
        else if (signifier == ClientToServerSignifier.AddToObserverSessionQueue)
        {
            int sessionIndex = int.Parse(data[1]);
            Assert.IsTrue(sessionIndex <= sessions.Count - 1, "Error: Out of bounds except when adding observer to session queue!");

            sessions[sessionIndex].observerIds.Add(id);

            string _msg = GetParsedBoardData(sessions[sessionIndex]);

            NewServerMessage(GetUserName(id) + ": joined game room " + sessionIndex.ToString() + " as an observer.");

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

            NewServerMessage(GetUserName(id) + ": left the session.");
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

            NewServerMessage(GetUserName(id) + ": left server");

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
            RemoveUsernameWithID(id);

            // Remove the client from our list
            RemoveClientAt(id);

            // Refresh the sessions available on client side
            NotifyClientsAboutSessionUpdate();
        }
        else if (signifier == ClientToServerSignifier.SendRecord)
        {


            int index = 2;
            int numSubDivisions = int.Parse(data[1]);
            for(int i = 0; i < numSubDivisions; i++)
            {
                string[] gameData = data[index].Split('+');

                Record r = new Record();
                r.DeserializeData(gameData);

                clientRecords.Add(r);
                index++;
            }


        }
        else if (signifier == ClientToServerSignifier.RecordSendingDone)
        {

            NewServerMessage(GetUserName(id) + ": new recording recieved.");

            Recording recording = new Recording();
            recording.timeRecorded = data[1];
            recording.username = data[2];

            // Copy the list over.
            // CopyTo method doesn't let you copy it to a list, so thats not an option
            foreach (Record r in clientRecords)
                recording.records.Add(r);

            // Add our recording
            recordings.Add(recording);


            // Send a copy of our recordings back to every client so they have them.
            NotifyClientsAboutNewRecordings();
        }
    }
    public void RemoveUsernameWithID(int id)
    {
        Client temp = null;
        foreach (Client c in clients)
        {
            if (c.connectionId == id)
            {
                temp = c;
                break;
            }
        }
        foreach (PlayerAccount account in activeAccounts)
        {
            if (account.username == temp.username)
            {
                activeAccounts.Remove(account);
                break;
            }
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
    public void NotifyClientsAboutNewRecordings()
    {
        // send all records to the server
        SendMessageToAllClients(ServerToClientSignifier.QueueStartOfRecordings.ToString() + ",");

        for (int i = 0; i < recordings.Count; i++)
        {
            int subDivisions = MaxElementsPerRecord;
            int subDivisionsPerList = (recordings[i].records.Count / subDivisions) + 1;


            // Add to the list of records:
            List<Record> tempRecords = new List<Record>();

            // In all of our records (seperated by "subdivision count")
            for (int j = 0; j < subDivisionsPerList; j++)
            {
                int indexStart = j * subDivisions;
                int indexEnd = (j + 1) * subDivisions;

                tempRecords.Clear();

                // For every record in this sub divided list
                for (int k = indexStart; k < indexEnd; k++)
                {
                    // Were sub dividing by a floored number so we will always have a remainder
                    // of elements after the last sub division, just do a simple check
                    if (k < recordings[i].records.Count)
                    {
                        tempRecords.Add(recordings[i].records[k]);
                    }
                }
                // parse the data into comma seperated values
                string msg = ServerToClientSignifier.SendRecording + "," + tempRecords.Count.ToString() + ",";

                // Get parsed data returns the board slots as '|' seperated values.
                // We can have seperated values inside other seperated values.
                // a comma will seperate the records, while a '|' will seperate the board slots themselves
                foreach (Record r in tempRecords)
                    msg += r.SerializeData() + ",";


                // and now we can send it to the client
                SendMessageToAllClients(msg);


            }
            // tell the server that were done sending records
            // so the server can add it to the list of saved recordings

            // Mark the end of this record list:
            SendMessageToAllClients(ServerToClientSignifier.QueueEndOfRecord + "," + recordings[i].timeRecorded + "," + recordings[i].username + ",");
            
        }
        SendMessageToAllClients(ServerToClientSignifier.QueueEndOfRecordings + ",");

    }

    private void SendMessageToAllClients(string msg)
    {
        foreach(Client c in clients)
        {
            SendMessageToClient(msg, c.connectionId);
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

    public bool IsUserNameAlreadyLoggedOn(string username)
    {
        foreach(PlayerAccount pa in activeAccounts)
            if (pa.username == username)
                return true;
        
        return false;
    }
    public string GetUserName(int id)
    {
        foreach (Client c in clients)
            if (c.connectionId == id)
                return c.username;

        return string.Empty;
    }
    public int GetIdFromUserName(string user)
    {
        foreach (Client c in clients)
            if (c.username == user)
                return c.connectionId;

        return -1;
    }
    public void DisconnectAllClients()
    {
        SendMessageToAllClients(ServerToClientSignifier.ConnectionLost.ToString() + ",");
    }
}
