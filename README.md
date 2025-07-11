# Clone Dash

[![Discord Invite](https://img.shields.io/discord/1332799392729665556?label=Discord&style=flat-square)](https://discord.gg/s98G9dKxHB)

This is a WIP open-source and cross-platform clone of 'Muse Dash', a parkour-rhythm game combination. It can currently load the base game levels and provides the core gameplay functionality. The goals are to provide various accessibility, training, and customizability features that the base game does not.

This is what I'd consider "vertical slice 1" of the project - almost everything works to some varying extent but there's a lot that still needs to happen. Contributions are welcome if you're willing and can understand the engine! I'm willing to explain anything in the Discord if need be.

## Screenshots & Videos

[![Clone Dash Preview Video](https://img.youtube.com/vi/3hFUoRz_uuk/0.jpg)](https://www.youtube.com/watch?v=3hFUoRz_uuk)
![image](https://github.com/user-attachments/assets/e87b782b-3a25-4000-acc4-b4b0f3a12877)
![image](https://github.com/user-attachments/assets/96cb2dd6-6d88-481c-8e37-2ed1fbb19d3c)
![image](https://github.com/user-attachments/assets/bd1d2f4a-c198-4ec5-92a9-33e5605e9103)
![image](https://github.com/user-attachments/assets/5ba660e7-eec5-4361-b367-4aa6471c0390)
![image](https://github.com/user-attachments/assets/992a5a89-3c25-4568-98ae-2fb4190e81ed)
![image](https://github.com/user-attachments/assets/55139795-eb76-4bed-9a28-984203332c55)


## Notes

While a lot of effort has gone into this current vertical slice of the project, there's still a lot of work that needs to be done - file formats may change, etc. So don't expect anything to be 100% static yet - even though this is a main-branch version of the project, we're still very much so in beta here...

You will need to own the game to play this currently, as it relies on finding a valid Steam installation of the game. Custom albums are supported (in the https://github.com/MDMods/CustomAlbums .mdm format), your mileage may vary. Custom albums can also be loaded via https://mdmc.moe from the main menu.

## Building
- Make sure you have Visual Studio 2022 or some IDE you like for C# development
- Make sure you have .NET 8.0
- Clone the repository, build the game, it should just work

If there's any issues, let me know and I'll try to resolve them. The game targets Windows, OSX, and Linux - with varying levels of support for each (Windows is the primary target, Linux secondary, OSX last - I don't have an M-series Mac to even test with, so mileage *really* might vary there...)

## Credits/Attribution

AssetStudio is licensed under the MIT license: https://github.com/Perfare/AssetStudio.

OdinSerializer is licensed under the Apache 2.0 license: https://github.com/TeamSirenix/odin-serializer..

I used this custom version of OdinSerializer built to be independent of Unity, which you can find here: https://github.com/wqaetly/OdinSerializerForNetCore

Raylib and Raylib-cs are licensed under the ZLib license: 

https://github.com/raysan5/raylib

https://github.com/ChrisDill/Raylib-cs

punch.wav: Cartoon_Punch_05.wav by RSilveira_88 -- https://freesound.org/s/216199/ -- License: Attribution 4.0
