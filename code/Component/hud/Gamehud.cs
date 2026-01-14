// GameHud.cs
using Sandbox;
using Sandbox.UI;
using Sandbox.UI.Construct;
using Dodgeball.Network;
using System;
namespace Dodgeball.UI
{
        public sealed class GameHud : PanelComponent
        {
                public static GameHud Instance { get; private set; }

                // Buffer si un message arrive avant que l'UI soit construite
                public static string PendingMessage = "";
                public static string[] PendingNames = new string[0];    
                private Panel playersListContent;
                private readonly Dictionary<string, Label> _nameToLabel = new();
                
                private Label ballIndicator;
private Behavior cachedBallBehavior;
[Property] public CameraComponent Camera { get; set; }

private readonly HashSet<string> _deadNames = new();

                private Label _centerLabel;
                private Panel playersListPanel;

                protected override void OnTreeFirstBuilt()
                {
                        base.OnTreeFirstBuilt();



                       var ownerPlayer = GameObject.GetComponentInParent<Bbplayer>();
                        if ( ownerPlayer != null && ownerPlayer.IsProxy )
                        {
                        Enabled = false;
                        return;
                        }

                        Instance = this;
                        
                        Panel.AddClass( "gamehud-root" );
                        Panel.StyleSheet.Load( "ui/GameHud.scss", inheritVariables: true, failSilently: false );

                        _centerLabel = Panel.Add.Label( "", "center-text" );
                        playersListPanel = Panel.Add.Panel( "players-list" );
                        // fond unique
                        playersListPanel.Add.Panel( "players-list-bg" );
                        
                        ballIndicator = Panel.Add.Label( "▲", "ball-indicator" );
ballIndicator.Style.Display = DisplayMode.None;
ballIndicator.Style.Dirty();
                        // contenu (labels)
                        playersListContent = playersListPanel.Add.Panel( "players-list-content" );
                        SetMessage( "" );
                        if ( PendingNames != null )
                        {
                                SetPlayerNames( PendingNames );
                                PendingNames = null;
                        }
                        Log.Info( "[GameHud] Built" );
                        NetcoManager.RequestLobbySnapshotFromHud();
                     
                }

                protected override void OnUpdate()
{
	base.OnUpdate();
	UpdateBallIndicator();
}

private void UpdateBallIndicator()
{
	if ( ballIndicator == null )
		return;

	var ballPos = GetBallWorldPosition();
	if ( ballPos == null )
	{
		HideBallIndicator();
		return;
	}

                // Main camera
                var cam = Camera;
        if ( cam == null || !cam.IsValid() )
        {
                HideBallIndicator();
                return;
        }


var hudSize = Panel.Box.Rect.Size;
if ( hudSize.x <= 1 || hudSize.y <= 1 )
{
	HideBallIndicator();
	return;
}

var half = hudSize * 0.5f;




	// Derrière la caméra ?
	var to = ballPos.Value - cam.Transform.Position;
	var z = to.Dot( cam.Transform.Rotation.Forward );
	var behind = z < 0;

	// Projection en coords normales [0..1] (peut dépasser si hors écran) :contentReference[oaicite:3]{index=3}
	var n = cam.PointToScreenNormal( ballPos.Value );

	// Si devant et dans l'écran => on cache
	if ( !behind && n.x >= 0 && n.x <= 1 && n.y >= 0 && n.y <= 1 )
	{
		HideBallIndicator();
		return;
	}

	// Direction depuis le centre écran
	var dir = (n - new Vector2( 0.5f, 0.5f ));
	if ( behind )
		dir = -dir;

	if ( dir.Length < 0.0001f )
	{
		HideBallIndicator();
		return;
	}

	dir = dir.Normal;

	// Position sur le bord de l'écran
	var margin = 40f;
	var edge = half - new Vector2( margin, margin );

	float tx = MathF.Abs( dir.x ) < 0.0001f ? float.MaxValue : edge.x / MathF.Abs( dir.x );
	float ty = MathF.Abs( dir.y ) < 0.0001f ? float.MaxValue : edge.y / MathF.Abs( dir.y );
	float t = MathF.Min( tx, ty );

	var pos = half + dir * t;

	// Rotation du triangle (▲ pointe vers le haut)
	var angleDeg = MathF.Atan2( dir.y, dir.x ) * (180.0f / MathF.PI) + 90.0f;

	ballIndicator.Style.Left = Length.Pixels( pos.x );
	ballIndicator.Style.Top = Length.Pixels( pos.y );
	ballIndicator.Style.Set( "transform", $"translate(-50%,-50%) rotate({angleDeg}deg)" );
	ballIndicator.Style.Display = DisplayMode.Flex;
	ballIndicator.Style.Dirty();
}

private Vector3? GetBallWorldPosition()
{
	// Cache la balle
	if ( cachedBallBehavior == null || !cachedBallBehavior.IsValid() )
	{
		var ballGo = Scene.GetAllObjects( true )
			.FirstOrDefault( go => go.Components.Get<Behavior>() != null );

		cachedBallBehavior = ballGo?.Components.Get<Behavior>();
	}

	if ( cachedBallBehavior == null || !cachedBallBehavior.IsValid() )
		return null;

	// Si ton Behavior a SyncedPosition, préfère-le (plus propre en proxy)
	// Sinon, prends la position de l’objet
	return cachedBallBehavior.SyncedPosition; // si ça compile pas -> remplace par cachedBallBehavior.WorldPosition / cachedBallBehavior.Transform.Position
}


private void HideBallIndicator()
{
	if ( ballIndicator.Style.Display != DisplayMode.None )
	{
		ballIndicator.Style.Display = DisplayMode.None;
		ballIndicator.Style.Dirty();
	}
}


                public void SetMessage( string msg )
                {
                        PendingMessage = msg ?? "";

                        if ( _centerLabel == null )
                                return;

                        _centerLabel.Text = PendingMessage;
                        _centerLabel.SetClass( "hidden", string.IsNullOrEmpty( PendingMessage ) );
                }

               public void SetPlayerNames( string[] names )
{
    PendingNames = names ?? System.Array.Empty<string>();

    if ( playersListPanel == null )
        return;

playersListContent?.DeleteChildren();
    _nameToLabel.Clear();

    foreach ( var name in PendingNames )
    {
        var label = playersListContent.Add.Label( name, "player-name" );

        // applique l'état mort si connu
        label.SetClass( "dead", _deadNames.Contains( name ) );

        _nameToLabel[name] = label;
    }
}

public void SetPlayerDeadByName( string displayName, bool isDead )
{
    if ( string.IsNullOrWhiteSpace( displayName ) )
        return;

    if ( isDead ) _deadNames.Add( displayName );
    else _deadNames.Remove( displayName );

    if ( _nameToLabel.TryGetValue( displayName, out var label ) )
    {
        label.SetClass( "dead", isDead );
    }
}



                public void SetPlayerDead( string connectionId, bool isDead )
                {
                        foreach ( var child in playersListPanel.Children )
                        {
                                if ( child is Label label )
                                {
                                        if ( label.Text.StartsWith( $"{connectionId} - " ) )
                                        {
                                                label.SetClass( "dead", isDead );
                                                break;
                                        }
                                }
                        }
                }       
        }
}
