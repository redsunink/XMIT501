# XMIT501
A Compact Tactical Radio application for simplistic multilayer communication.

This application was created with Google Gemini assistance (AI).

XMIT-501 is a low-latency high performance voice application made to imitate the radio communications and introduce multiple comm layers with least possible effort ("zero-IQ enduser policy"), opposed to (but not replacing) software like Discord, TeamSpeak or Mumble.
It behaves as pseudo-simplex (one-way) radio as a creative constraint, enabling communication over 10 channels with arbitrary selection of reception and transmission channels and benefits from structured communication rules and radio discipline.
Basic package includes both client and server. Headless relay is available as source for hosting on server machines.
XMIT-501 supports wide variety of input methods like joystick, HOTAS, steering wheels etc (DirectInput).
In server mode, application benefits from router UPnP to avoid port forwarding hassle.
It does not use any encryption or protection; your voice is transmitted over UDP using real-time transport protocol, encoded with Opus codec.

# Quick Start
You will need .NET 8.0 installed; you will be prompted on launch if it's missing (this should direct you to Windows Store or website to install it momentarily - 55 MB download).
After launch, press on PARAM knob/button to open settings window.
Set your global transmit hotkey, and separate Ch1/Ch2/Ch3 hotkeys if needed.
If you wish to connect to a server, put IP:port in corresponding field and press "Apply&Close".
If you wish to act as a server, put any IP (like localhost - 127.0.0.1 or 255.255.255.255 or anything) and port you wish server to listen to (defaults to 5000) in same manner (e.g. 127.0.0.1:5000) and check "Host server" checkbox. Press "Apply&Close".
After closing setting window, press turnswitch on top right to "LIVE".
This should enable "CONNECTED" light and a screen, as well as background static SFX. In a few seconds it should show status and ping for client e.g. "CONNECTED [20MS]", or "DISPATCH[OPEN]" for a successful server start.
You now may select your reception channels ("RECV CHL") and transmission channels ("XMIT CHL") and use application.
Turn off "STATIC" and "BLIP" to disable SFX respectively.
