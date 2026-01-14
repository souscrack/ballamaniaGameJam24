// Gamemode.cs
using Sandbox;
using System;
using System.Linq;
using System.Collections.Generic;
using Dodgeball.Network;

namespace Dodgeball.UI;

public sealed class GameMode : Component, Component.INetworkListener
{
	public static GameMode Instance { get; private set; }

	[Property] public float RoundEndDelay { get; set; } = 3.0f;
	[Property] public float RoundRestartDelay { get; set; } = 1.0f;

	[Property] public float CountdownStepSeconds { get; set; } = 1.0f;

private int _countdownValue = 3;
private float _nextCountdownAt = 0f;


	// Optionnel : assigne la balle dans l’inspector. Sinon on la cherche.
	[Property] public GameObject BallObject { get; set; }

	private enum MatchState
	{
		Waiting,
		Countdown,
		Playing,
		Ending
	}

	private MatchState _state = MatchState.Waiting;
	private TimeSince _stateTime;

private List<GameTransform> _spawns = new();

protected override void OnStart()
{
	if ( IsProxy ) return;

	Instance = this;
	ResetMatchState();
}

protected override void OnEnabled()
{
	if ( IsProxy ) return;

	Instance = this;
	ResetMatchState();
}

protected override void OnDisabled()
{
	if ( IsProxy ) return;

	SetBallActive( false );

	if ( Instance == this )
		Instance = null;
}

private void ResetMatchState()
{
	_state = MatchState.Waiting;
	_stateTime = 0;

	CacheSpawns();

	NetcoManager.ServerResetMatch( resetScores: true );

	SetBallActive( false );
	UpdateWaitingMessage();
}
	protected override void OnDestroy()
	{
		if ( Instance == this ) Instance = null;
	}

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;

		// Réparer les cas join/leave : si pas assez de joueurs => waiting
		if ( !HasEnoughPlayers() )
		{

			SetBallActive( false );
			RecenterBall();
			if ( _state != MatchState.Waiting )
			{
				_state = MatchState.Waiting;
				_stateTime = 0;
				
			}

			UpdateWaitingMessage();
			return;
		}

		switch ( _state )
		{
			case MatchState.Waiting:
				SetBallActive( false );
				RecenterBall();
				BeginCountdown();
				break;
			case MatchState.Countdown:
				SetBallActive( false );
    			RecenterBall();
				UpdateCountdown();
				break;


			case MatchState.Playing:
				CheckRoundEnd();
				break;

			case MatchState.Ending:
				if ( _stateTime > RoundEndDelay )
	{
		// Si encore assez de joueurs -> countdown next round
		if ( HasEnoughPlayers() )
			BeginCountdown();
		else
		{
			_state = MatchState.Waiting;
			_stateTime = 0;
			UpdateWaitingMessage();
		}
	}
				break;
		}
	}

	private void CacheSpawns()
	{
		_spawns = Scene.GetAllObjects( true )
			.Where( go => go.Tags.Has( "spawn" ) )
			.Select( go => go.Transform )
			.ToList();

		if ( _spawns.Count == 0 )
			Log.Warning( "[GameMode] Aucun spawn trouvé (tag 'spawn')." );
	}

	private bool HasEnoughPlayers()
	{
		var min = NetcoManager.Instance != null ? NetcoManager.Instance.MinPlayersToStart : 2;
		return Connection.All.Count >= min;
	}

	private void UpdateWaitingMessage()
	{
		var min = NetcoManager.Instance != null ? NetcoManager.Instance.MinPlayersToStart : 2;
		NetcoManager.HudMessageAll( $"En attente de joueurs ({Connection.All.Count}/{min})" );
	}


	private void StartRound()
	{
		_state = MatchState.Playing;
		_stateTime = 0;

		// Clear message d’attente
		NetcoManager.HudMessageAll( "" );
		

		// Met tout le monde vivant + spawn
		RespawnAllPlayersAlive();

		// Lance la balle
		SetBallActive( true );

		// Refresh HUD (noms + scores)
		NetcoManager.ServerRefreshLobbyState();
	}

	private void RespawnAllPlayersAlive()
	{
		if ( _spawns.Count == 0 ) return;

		var players = Scene.GetAllComponents<Bbplayer>()
			.Where( p => p != null && p.IsValid() )
			.ToList();

		int i = 0;

		foreach ( var conn in Connection.All )
		{
			var p = players.FirstOrDefault( pl => pl.GameObject.Network.OwnerConnection == conn );
			if ( p == null ) continue;

			var spawn = _spawns[i % _spawns.Count];
			i++;

			// Respawn serveur + HUD alive
			p.ServerRespawnAt( spawn.Position, spawn.Rotation );

			NetcoManager.RpcSetDeadState( NetcoManager.IdString( conn ), false );
		}
	}

	private void CheckRoundEnd()
	{
		// Dernier vivant = gagne
		var alive = NetcoManager.GetAliveConnections().ToList();
		if ( alive.Count > 1 ) return;

		_state = MatchState.Ending;
		_stateTime = 0;

		SetBallActive( false );
		RecenterBall();

		if ( alive.Count == 1 )
		{
			var winner = alive[0];
			NetcoManager.ServerAddPoint( winner );
			NetcoManager.HudMessageAll( $"{winner.DisplayName} gagne +1" );
		}
		else
		{
			NetcoManager.HudMessageAll( "Égalité" );
		}

		NetcoManager.ServerRefreshLobbyState();
	}

	private void SetBallActive( bool active )
	{
		var ball = BallObject;

		if ( ball == null )
		{
			// Fallback : trouve le premier objet qui a Behavior
			ball = Scene.GetAllObjects( true )
				.FirstOrDefault( go => go.Components.Get<Behavior>() != null );
		}

		var behavior = ball?.Components.Get<Behavior>();
		if ( behavior == null ) return;

		//behavior.RpcSetRoundActive( active );

		// Reset serveur au start
		if ( active )
			behavior.ServerReturnHome();
			behavior.ServerKickoffRound();
	}

	public void OnActive( Connection connection )
	{
		if ( IsProxy ) return;

		NetcoManager.ServerRefreshLobbyState();

		// Si on est en waiting, met à jour le message
		if ( _state == MatchState.Waiting )
			UpdateWaitingMessage();
	}

	public void OnDisconnected( Connection connection )
	{
		if ( IsProxy ) return;

		NetcoManager.ServerRemovePlayer( connection );
		NetcoManager.ServerRefreshLobbyState();

		if ( !HasEnoughPlayers() )
		{
			_state = MatchState.Waiting;
			_stateTime = 0;
			SetBallActive( false );
			UpdateWaitingMessage();
		}
	}

	private void BeginCountdown()
{
	_state = MatchState.Countdown;
	_stateTime = 0;

	// Tout le monde en vie + sur spawn
	RespawnAllPlayersAlive();

	// Balle au centre + inactive pendant le countdown
	SetBallActive( false );
	RecenterBall();

	_countdownValue = 3;
	_nextCountdownAt = CountdownStepSeconds;

	NetcoManager.HudMessageAll( "3" );
	NetcoManager.ServerRefreshLobbyState();
}
private void RecenterBall()
{
	var behavior = FindBallBehavior();
	if ( behavior == null ) return;

	behavior.ServerReturnHome();
}
private Behavior FindBallBehavior()
{
	var ball = BallObject;

	if ( ball == null )
	{
		ball = Scene.GetAllObjects( true )
			.FirstOrDefault( go => go.Components.Get<Behavior>() != null );
	}

	return ball?.Components.Get<Behavior>();
}
private void UpdateCountdown()
{
	// tick toutes les 1s (CountdownStepSeconds)
	if ( _stateTime < _nextCountdownAt )
		return;

	_countdownValue--;

	if ( _countdownValue > 0 )
	{
		NetcoManager.HudMessageAll( $"{_countdownValue}" );
		_nextCountdownAt += CountdownStepSeconds;
		return;
	}

	if ( _countdownValue == 0 )
	{
		NetcoManager.HudMessageAll( "GO" );
		_nextCountdownAt += CountdownStepSeconds;
		return;
	}

	// Après "GO" affiché 1 step -> start round
	NetcoManager.HudMessageAll( "" );
	_state = MatchState.Playing;
	_stateTime = 0;

	SetBallActive( true );
}

}
