# arcane-networking
A networking solution made for godot that aim's to have similar functionality and useability as Mirror Networking from Unity

IMPORTANT ! FOR DEPENDENCIES TO WORK, 
ADD THIS TO THE BOTTOM OF YOUR <GODOT_PROJECT_NAME>.csproj FILE!!
\/\/\/\/
<ItemGroup>
    <Reference Include="Steamworks.NET">
        <HintPath>.\addons\arcane-networking\lib\Steamworks.NET.dll</HintPath>
    </Reference>
    <Reference Include="MessagePack">
        <HintPath>.\addons\arcane-networking\lib\MessagePack.dll</HintPath>
    </Reference>
    <Reference Include="Mono.Cecil">
        <HintPath>.\addons\arcane-networking\plugin\lib\Mono.Cecil.dll</HintPath>
    </Reference>
    <Reference Include="Mono.Cecil.Rocks">
        <HintPath>.\addons\arcane-networking\plugin\lib\Mono.Cecil.Rocks.dll</HintPath>
    </Reference>
</ItemGroup>