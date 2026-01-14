// NetcoManager.cs
using Sandbox;
using System.Linq;
using Dodgeball.UI;
using System.Collections.Generic;

namespace Dodgeball.Network;
public sealed class NetcoManager : Component
{
	[Property] public int MinPlayersToStart { get; set; } = 2;
	private static readonly HashSet<string> DeadPlayerIds = new();
	private static readonly Dictionary<string, int> ScoreById = new();
	private static NetcoManager _instance;
	public static NetcoManager Instance => _instance;


	protected override void OnStart()
	{
		// Singleton simple (une seule instance dans la scène)
		if ( _instance != null && _instance != this )
		{
			Log.Warning( "[NetcoManager] Multiple instances detected. Keeping first one." );
            GameObject.Destroy();
			return;
		}

		_instance = this;
		GameObject.Network.AssignOwnership( Connection.Host );
	}

	protected override void OnDestroy()
{
	if ( _instance == this )
		_instance = null;
}
private static NetcoManager EnsureInstance()
{
	// 1) si _instance existe mais est invalide (hot reload / destroy)
	if ( _instance != null && !_instance.IsValid() )
		_instance = null;

	// 2) si déjà OK
	if ( _instance != null )
		return _instance;

	// 3) tente de retrouver une instance existante dans la scène (évite doublons)
	_instance = Game.ActiveScene?.GetAllObjects( true )?
    .Select( go => go.Components.Get<NetcoManager>() )
    .FirstOrDefault( nc => nc != null && nc.IsValid() );

	if ( _instance != null )
		return _instance;

	// 4) autospawn
	var go = new GameObject( true, "NetcoManager (AutoSpawn)" );
	_instance = go.Components.Create<NetcoManager>();
	Log.Info( "[NetcoManager] AutoSpawned." );

	return _instance;
}

public static string IdString( Connection c ) => c.Id.ToString("N");

public static IEnumerable<Connection> GetAliveConnections()
{
	return Connection.All.Where( c => !DeadPlayerIds.Contains( IdString( c ) ) );
}

public static void ServerAddPoint( Connection winner )
{
	var netco = EnsureInstance();
	if ( netco == null || !netco.IsValid() ) return;
	if ( netco.IsProxy ) return; // host only
	if ( winner == null ) return;

	var id = IdString( winner );

	if ( !ScoreById.TryGetValue( id, out var score ) )
		score = 0;

	ScoreById[id] = score + 1;
}

public static void ServerRemovePlayer( Connection c )
{
	var netco = EnsureInstance();
	if ( netco == null || !netco.IsValid() ) return;
	if ( netco.IsProxy ) return; // host only
	if ( c == null ) return;

	var id = IdString( c );
	DeadPlayerIds.Remove( id );
	ScoreById.Remove( id );
}



public static void RequestLobbySnapshotFromHud()
{
	var netco = EnsureInstance();
	if ( netco == null || !netco.IsValid() )
		return;

	// Si on est host/local (pas proxy), on peut refresh direct
	if ( !netco.IsProxy )
	{
		ServerRefreshLobbyState();
		return;
	}

	// Sinon, on demande au serveur via RPC
	netco.RpcRequestLobbySnapshot();
}

[Rpc.Owner]
private void RpcRequestLobbySnapshot()
{
	ServerRefreshLobbyState();
}

	public static void ServerRefreshLobbyState()
	{
		var netco = EnsureInstance();
		if ( netco == null || !netco.IsValid() )
			return;

		if ( netco.IsProxy )
			return;
		var names = Connection.All.Select( c =>
{
	var id = IdString( c );
	ScoreById.TryGetValue( id, out var score );
	return $"{c.DisplayName}  {score}";
}).ToArray();
		var packed = string.Join( "\n", names ); // pack en string
		UpdatePlayerList( packed );

		
	}


[Rpc.Broadcast]
public static void UpdatePlayerList( string packedNames )
{
    var names = string.IsNullOrEmpty( packedNames )
        ? System.Array.Empty<string>()
        : packedNames.Split( '\n', System.StringSplitOptions.RemoveEmptyEntries );

    // si HUD pas prêt, buffer
    if ( GameHud.Instance == null )
    {
        GameHud.PendingNames = names;
        return;
    }

    GameHud.Instance.SetPlayerNames( names );
}

	[Rpc.Broadcast]
	public static void HudMessageAll( string message )
	{
		if ( GameHud.Instance == null )
		{
			GameHud.PendingMessage = message;
			return;
		}
		GameHud.Instance.SetMessage( message );
	}

	public static void ServerMarkPlayerDead( Connection conn )
{
	Log.Info( $"Marking player dead: {conn.Name}" );
	ServerSetPlayerDeadState( conn, true );
}

public static void ServerMarkPlayerAlive( Connection conn )
{
	ServerSetPlayerDeadState( conn, false );
}

private static void ServerSetPlayerDeadState( Connection conn, bool isDead )
{
	var id = conn.Id.ToString("N");
	var netco = EnsureInstance();
	if ( netco == null || !netco.IsValid() ) return;
	if ( netco.IsProxy ) return; // host only
	if ( conn == null ) return;

	if ( isDead ) DeadPlayerIds.Add( id );
	else DeadPlayerIds.Remove( id );

	// update unitaire
	UpdatePlayerDeadState( id, isDead );
}
private static void BroadcastDeadSnapshot()
{
	var netco = EnsureInstance();
	if ( netco == null || !netco.IsValid() ) return;
	if ( netco.IsProxy ) return; // host only

	// format: "id|0/1" par ligne
	var packed = string.Join( "\n",
		Connection.All.Select( c => $"{c.Id}|{(DeadPlayerIds.Contains(c.Id.ToString("N")) ? 1 : 0)}" )
	);

	UpdateDeadSnapshot( packed );
}

[Rpc.Broadcast]
public static void UpdatePlayerDeadState( string connectionId, bool isDead )
{
	// HUD pas prêt -> tu peux buffer côté HUD si tu veux
	if ( GameHud.Instance == null ) return;

	// IMPORTANT: il faut une fonction côté HUD
	GameHud.Instance.SetPlayerDead( connectionId, isDead );
}

[Rpc.Broadcast]
public static void UpdateDeadSnapshot( string packed )
{
	if ( GameHud.Instance == null ) return;

	if ( string.IsNullOrEmpty( packed ) ) return;

	// applique tout
	foreach ( var line in packed.Split( '\n', System.StringSplitOptions.RemoveEmptyEntries ) )
	{
		var parts = line.Split( '|' );
		if ( parts.Length != 2 ) continue;

		var id = parts[0];
		if ( int.TryParse( parts[1], out var deadInt ) )
		{
			GameHud.Instance.SetPlayerDead( id, deadInt == 1 );
		}
	}
}

[Rpc.Broadcast]
public static void UpdatePlayerDeadStateByName( string displayName, bool isDead )
{
    if ( GameHud.Instance == null ) return;
    GameHud.Instance.SetPlayerDeadByName( displayName, isDead );
}
[Rpc.Host]
public static void RpcSetDeadState( string connectionId, bool isDead )
{
    if ( string.IsNullOrEmpty( connectionId ) )
        return;

    if ( isDead ) DeadPlayerIds.Add( connectionId );
    else DeadPlayerIds.Remove( connectionId );

    // Convertit l'id -> DisplayName pour ton HUD actuel (indexé par nom)
    var conn = Connection.All.FirstOrDefault( c => c.Id.ToString("N") == connectionId );
    var displayName = conn?.DisplayName;

    if ( !string.IsNullOrEmpty( displayName ) )
        UpdatePlayerDeadStateByName( displayName, isDead );
}

public static void ServerResetMatch( bool resetScores )
{
	var netco = EnsureInstance();
	if ( netco == null || !netco.IsValid() ) return;
	if ( netco.IsProxy ) return; // host only

	DeadPlayerIds.Clear();

	if ( resetScores )
		ScoreById.Clear();

	// remet tout le monde vivant côté HUD (par nom, puisque ton HUD est basé sur le DisplayName)
	foreach ( var c in Connection.All )
	{
		UpdatePlayerDeadStateByName( c.DisplayName, false );
	}

	HudMessageAll( "" );
	ServerRefreshLobbyState();
}




}