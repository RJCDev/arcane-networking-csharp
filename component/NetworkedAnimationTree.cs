using ArcaneNetworking;
using Godot;
using Godot.Collections;
using System;

[GlobalClass]
public partial class NetworkedAnimationTree : NetworkedComponent
{
	[Export] public AnimationTree AnimTree;
	Dictionary<byte, string> _conditions = []; // Conditions map
	public AnimationNodeStateMachinePlayback Playback => (AnimationNodeStateMachinePlayback)AnimTree.Get("parameters/playback");

	public override void _Ready()
	{
		_conditions.Clear();
		
		byte index = 0;

		foreach (Dictionary prop in AnimTree.GetPropertyList())
		{
			string name = prop["name"].AsString();

			if (!name.StartsWith("parameters/"))
				continue;

			_conditions[index] = name;
			index++;
		}
	}

	byte GetConditionByte(string c)
	{
		foreach (var condition in _conditions)
			if (condition.Value == c)
				return condition.Key;

		return byte.MaxValue;
	}

	// Blend
	public void SetBlend1D(string condition, float value)
	{
		if (AuthorityMode == AuthorityMode.Server)
			RelaySetBlend1D(GetConditionByte(condition), value);
		else 
			CommandSetBlend1D(GetConditionByte(condition), value);
	}

	[Command]
	void CommandSetBlend1D(byte condition, float value)
		=> RelaySetBlend1D(condition, value);

	[Relay]
	void RelaySetBlend1D(byte condition, float value)
		=> AnimTree.Set(_conditions[condition], value);
	
	// Bool
	public void SetBool(string condition, bool value)
	{
		if (AuthorityMode == AuthorityMode.Server)
			RelaySetBool(GetConditionByte(condition), value);
		else 
			CommandSetBool(GetConditionByte(condition), value);
	}

	[Command]
	void CommandSetBool(byte condition, bool value)
		=> RelaySetBool(condition, value);

	[Relay]
	void RelaySetBool(byte condition, bool value)
		=> AnimTree.Set(_conditions[condition], value);

	public void Travel(string state)
	{
		if (AuthorityMode == AuthorityMode.Server)
			CommandTravel(state);
		else 
			RelayTravel(state);
	}
	[Command]
	void CommandTravel(string condition)
		=> RelayTravel(condition);
	
	[Relay]
	void RelayTravel(string condition)
		=> Playback.Travel(condition);
	

}
