# 📻 XMIT-501 Tactical Radio
**A Compact Tactical Radio application for seamless multilayer communication.**

> *Developed with assistance from Google Gemini AI.*

XMIT-501 is a low-latency, high-performance voice application designed to simulate authentic radio environments. It introduces multiple communication layers with minimal effort (**"Zero-IQ Enduser Policy"**), serving as a tactical alternative to (but not a replacement for) software like Discord, TeamSpeak, or Mumble.

---

### 🛡️ Core Philosophy
* **Pseudo-Simplex:** Operates as a one-way radio by design to encourage structured communication and radio discipline.
* **10-Channel Matrix:** Supports arbitrary selection of reception and transmission channels.
* **Universal Input:** Native support for Joysticks, HOTAS, and steering wheels via DirectInput.
* **Zero-Config Hosting** (for home use): Integrated **UPnP** support to bypass manual port forwarding on consumer-grade routers.
* **High Fidelity:** Audio is transmitted over UDP using the **Opus** codec for quality tactical comms.
* **Protocol simplicity:** The only packets on your network are UDP with actual voice and 1-byte channel identifier. Lowest possible ping and bandwidth usage.
* **Low-level networking:** XMIT-501 will work over internet, LAN or any other network that will transport UDP - like OpenVPN, RadminVPN, Hamachi etc.

---

### ⚠️ Disclaimer
This is experimental software provided "as is" without warranty of any kind. 
XMIT-501 does not use encryption and does not give any means to conceal your IP address. Your voice data is transmitted in 
plain-text Opus packets over UDP. Use only on trusted networks. 
The developer is not responsible for any network instability or 
hardware issues arising from the use of this software.
THIS APPLICATION **DOES NOT** COLLECT OR SEND ANY OF YOUR DATA TO AUTHOR OR THIRD PARTY
YOU BEAR WHOLE RESPONSIBILITY FOR OPENING YOUR PORTS TO THE WORLD AND/OR SHARING YOUR ACTUAL IP ADDRESS

---

### 🚀 Quick Start
1. **Requirements:** You need **.NET 8.0** installed (the app will prompt you with a download link if it's missing).
2. **Setup:** Click the **PARAM** knob/button to open Settings.
3. **Keybinds:** Map your **Global TX** and optional **Ch1/Ch2/Ch3** hotkeys.
4. **Connect:** - **Client:** Enter `IP:Port` (e.g., `1.2.3.4:5000`) and click Apply.
   - **Host:** Enter any IP and desired port (e.g., `127.0.0.1:5000`), check **Host Server**, and click Apply. This will use specified port to listen to.
5. **Power On:** Rotate the top-right switch to **LIVE**.
6. **Status:** The screen will display `CONNECTED [XX MS]` for clients or `DISPATCH [OPEN]` for hosts.

> **Tip:** You can toggle **STATIC** and **BLIP** switches to customize your audio feedback.
