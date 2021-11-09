# GAME3110 Server Template
## Client/Servers Features:
### Challenge 1: Player Communication
Players can communicate with other players in their game room, additionally they can also chat with observers, and players outside of the game room.
### Challenge 2: Observers
Clients can join any game room that is in session as an observer, further more, they can also communicate with the other clients in that room.
### Challenge 3: Replay system
Players who are 'not' observers will record their session as well as their player chat, you can then play it back in replay mode which can be found on the main menu.
### Optional Challenge: Server Console Log
The server log has text messages which get displayed whenever something changes in one of the clients. For example a client joining/leaving, making a board move, or sending a chat message. You also have a command system! You can kick and ban players from the session. Type "/help" in the server console to see a list of commands to choose from.
### Other features:
1. Connect to host menu - you can connect to a specific host using a specific ip and port.

2. Connection lost - if the server goes offline suddenly, all clients get kicked back to the connect to host menu.

3. Player connection lost - if a player disconnects mid game, the other player as well as all observers get kicked back to the find match menu.