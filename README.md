🌐 arcane-networking

Zero-allocation networking for Godot, designed to offer functionality and usability similar to Mirror Networking from Unity.

⚙️ Project Setup
__________________

Arcane Networking needs the following nodes in your project to function properly:

🧠 NetworkManager

Must persist for the entire lifetime of the game.

To add it as a global manager:

Go to Project → Project Settings → Globals.

Add your NetworkManager scene there.

📡 MessageLayer

Must also persist for the entire lifetime of the game.

Add one of the premade message layers as a child of NetworkManager:

KcpMessageLayer

SteamMessageLayer

SimulationMessageLayer

Or your own custom implementation

Drag the node into the MsgLayer export on the NetworkManager.

⚠️ Note: SimulationMessageLayer does not work on its own.
It must be used together with another MessageLayer.
See its documentation page for details.

🛠️ Optional Node
🧪 NetworkDebugGUI

Provides a simple debug interface.

Lets you:

🖥️ Host a server

🔗 Connect to a server

🔄 Run a host/client hybrid for testing

✅ Once these nodes are added and configured, your project will be ready to host and connect to servers.
