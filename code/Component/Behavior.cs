using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

public sealed class Behavior : Component
{


	[Property, Category( " Dodgeball" )]
	public float StartingBallSpeed { get; set; } = 400f;

	[Property, Category( " Dodgeball" )]
	public float MaxBallSpeed { get; set; } = 500f;

	[Property, Category( " Dodgeball" )]
	public float SpeedMultiplierOnHit { get; set; } = 1.05f;

	[Property, Category( " Dodgeball " )]
	public float DirectionRateBoost { get; set; } = 6.0f;

	[Property, Category( " Dodgeball" )]
	public float DirectionRateNormal { get; set; } = 1.75f;

	[Property, Category( " Dodgeball" )]
	public float DirectionBoostDuration { get; set; } = 0.05f;

	[Property, Category( " Dodgeball" )]
	public float SpeedChaseRate { get; set; } = 0.25f;

	[Property, Category( "Targeting" )]
	public float TargetEyeOffsetZ { get; set; } = 48f;

	[Property, Category( "Collision" )]
	public SphereCollider Hitbox { get; set; }

	[Property, Category( "Collision" )]
	public float GroundZ { get; set; } = 0f;

	[Property, Category( "Debug" )]
	public bool DebugTrajectory { get; set; } = false;

	[Property, Category( "FX" )]
	public string PunchSound { get; set; } = "bing";

	// ─────────────────────────────────────────────────────────────
	// SYNCED STATE (owner/host authoritative)
	// [Sync] ne peut être modifié que par le owner de l'objet.
	// Si l'objet n'a pas de owner, l'host le simule. :contentReference[oaicite:2]{index=2}
	// ─────────────────────────────────────────────────────────────

	[Sync] public Vector3 BallDirection { get; set; } = Vector3.Forward;
	[Sync] public float BallSpeed { get; set; } = 0f;
	[Sync] public bool BallIsOut { get; set; } = false;
	[Sync] public float DirBoostTimeLeft { get; set; } = 0f;

	// Si jamais GameObject ne sync pas chez toi, on passera à un ID réseau.
	[Sync] public GameObject TargetedPlayer { get; set; }
	[Sync] public GameObject PreviousTargetedPlayer { get; set; }

	// Position réseau (si tu n'as pas NetworkTransform)
	[Sync] public Vector3 SyncedPosition { get; set; }

	// ─────────────────────────────────────────────────────────────
	// LOCAL
	// ─────────────────────────────────────────────────────────────

	private List<GameObject> _targets = new();

	private Vector3 _homePos;
private Rotation _homeRot;

	private Vector3 _renderPos; // interpolation client

	protected override void OnStart()
	{
		_renderPos = WorldPosition;
			_homePos = WorldPosition;
	_homeRot = WorldRotation;

		// Initialiser la position sync côté simulateur
		if ( !IsProxy )
			SyncedPosition = 	WorldPosition;

		if ( !IsProxy )
			RefreshTargets();
	}

	protected override void OnFixedUpdate()
	{

		if ( IsProxy )
			return;

		if ( !BallIsOut || BallSpeed <= 0.001f )
		{
			SyncedPosition = WorldPosition;
			return;
		}

		float dt = Time.Delta;

		if ( TargetedPlayer == null || !TargetedPlayer.IsValid() )
			TargetedPlayer = AcquireTarget( PreviousTargetedPlayer, BallDirection );

		if ( TargetedPlayer != null && TargetedPlayer.IsValid() )
		{
			Vector3 desiredDir = (GetTargetEyePosition( TargetedPlayer ) - WorldPosition).Normal;
			float dirRate = GetCurrentDirectionRateAndTickTimer( dt );

			BallDirection = ChaseVector( BallDirection, desiredDir, dirRate, dt ).Normal;
		}

		BallSpeed = ChaseFloat( BallSpeed, MaxBallSpeed, SpeedChaseRate, dt );

		Vector3 nextPos = WorldPosition + BallDirection * BallSpeed * dt;

		HandleTerrainCollision( ref nextPos );

		WorldPosition = nextPos;
		SyncedPosition = nextPos;

		if ( DebugTrajectory )
			DrawDebug( "SIM" );
	}

	protected override void OnUpdate()
	{
		if ( !IsProxy )
			return;

		float lerp = 1f - MathF.Exp( -12f * Time.Delta );
		_renderPos = Vector3.Lerp( _renderPos, SyncedPosition, lerp );
		WorldPosition = _renderPos;

		if ( DebugTrajectory )
			DrawDebug( "PROXY" );
	}



	public void punch( Vector3 hitDirection, GameObject owner ) => Punch( hitDirection, owner );

	public void Punch( Vector3 hitDirection, GameObject owner )
	{
		if ( !IsProxy )
		{
			ApplyPunchSim( hitDirection, owner );
			return;
		}

		PunchRequest( hitDirection, owner );
	}

	public void ServerReturnHome()
{
	if ( IsProxy ) return;

	WorldPosition = _homePos;
	WorldRotation = _homeRot;

	SyncedPosition = WorldPosition;

	// stop mouvement physique si tu as un rigidbody
	var rb = Components.Get<Rigidbody>();
	if ( rb != null )
	{
		rb.Velocity = Vector3.Zero;
		rb.AngularVelocity = Vector3.Zero;
	}
}



	[Rpc.Owner]
	private void PunchRequest( Vector3 hitDirection, GameObject owner )
	{
		ApplyPunchSim( hitDirection, owner );
	}

	[Rpc.Broadcast]
	private static void PlayPunchFxAll( string soundName, Vector3 position )
	{
		Sound.Play( soundName, position );
	}

	private void ApplyPunchSim( Vector3 hitDirection, GameObject owner )
	{
		RefreshTargets();

		PreviousTargetedPlayer = TargetedPlayer;

		if ( !BallIsOut || BallSpeed <= 0.001f )
			BallSpeed = StartingBallSpeed;
		else
			BallSpeed = MathF.Min( MaxBallSpeed, BallSpeed * SpeedMultiplierOnHit );

		BallIsOut = true;

		if ( hitDirection.Length > 0.001f )
			BallDirection = hitDirection.Normal;

		TargetedPlayer = AcquireTarget( owner, BallDirection );

		DirBoostTimeLeft = DirectionBoostDuration;

		SyncedPosition = WorldPosition;

		PlayPunchFxAll( PunchSound, WorldPosition );
	}



	private void RefreshTargets()
	{
		_targets = Scene
			.GetAllObjects( true )
			.Where( go => go.IsValid() && go.Components.TryGet<Cible>( out _ ) )
			.ToList();
	}

	private Vector3 GetTargetEyePosition( GameObject target )
	{
		return target.WorldPosition + Vector3.Up * TargetEyeOffsetZ;
	}

	private GameObject AcquireTarget( GameObject exclude, Vector3 travelDir )
	{
		if ( _targets == null || _targets.Count == 0 )
			RefreshTargets();

		GameObject best = null;
		float bestDot = -1f;

		foreach ( var candidate in _targets )
		{
			if ( candidate == null || !candidate.IsValid() )
				continue;

			if ( exclude != null && candidate == exclude )
				continue;

			Vector3 toCand = GetTargetEyePosition( candidate ) - WorldPosition;
			if ( toCand.Length < 0.001f )
				continue;

			float dot = Vector3.Dot( travelDir.Normal, toCand.Normal );
			if ( dot > bestDot )
			{
				bestDot = dot;
				best = candidate;
			}
		}

		return best;
	}

	private float GetCurrentDirectionRateAndTickTimer( float dt )
	{
		if ( DirBoostTimeLeft > 0f )
		{
			DirBoostTimeLeft = MathF.Max( 0f, DirBoostTimeLeft - dt );
			return DirectionRateBoost;
		}

		return DirectionRateNormal;
	}

	private static Vector3 ChaseVector( Vector3 current, Vector3 target, float ratePerSecond, float dt )
	{
		Vector3 delta = target - current;

		float maxStep = MathF.Max( 0f, ratePerSecond ) * MathF.Max( 0f, dt );
		float len = delta.Length;

		if ( len <= 0.00001f || len <= maxStep )
			return target;

		return current + (delta / len) * maxStep;
	}

	private static float ChaseFloat( float current, float target, float ratePerSecond, float dt )
	{
		float maxStep = MathF.Max( 0f, ratePerSecond ) * MathF.Max( 0f, dt );
		float delta = target - current;

		if ( MathF.Abs( delta ) <= maxStep )
			return target;

		return current + MathF.Sign( delta ) * maxStep;
	}

	private void HandleTerrainCollision( ref Vector3 nextPos )
	{
		if ( Hitbox == null )
			return;

		float radius = Hitbox.Radius;

		if ( nextPos.z - radius <= GroundZ )
		{
			nextPos.z = GroundZ + radius;
			BallDirection = BallDirection.WithZ( MathF.Abs( BallDirection.z ) ).Normal;
		}
	}


	private void DrawDebug( string tag )
	{
		Vector3 pos = WorldPosition;

		DebugOverlay.Text(
			pos + Vector3.Up * 12f,
			$"[{tag}] Speed:{BallSpeed:0.0} DirRate:{(DirBoostTimeLeft > 0f ? DirectionRateBoost : DirectionRateNormal):0.00}"
		);

		DebugOverlay.Line( new Line( pos, pos + BallDirection * 80f ) );

		if ( TargetedPlayer != null && TargetedPlayer.IsValid() )
		{
			var eye = GetTargetEyePosition( TargetedPlayer );
			DebugOverlay.Line( new Line( pos, eye ) );
			DebugOverlay.Sphere( new Sphere( eye, 6f ) );
		}
	}

	public void ServerKickoffRound()
{
    if ( IsProxy ) return;

    RefreshTargets();

    PreviousTargetedPlayer = null;
    TargetedPlayer = AcquireTarget( null, Vector3.Forward );

    if ( TargetedPlayer != null && TargetedPlayer.IsValid() )
    {
        var dir = (GetTargetEyePosition( TargetedPlayer ) - WorldPosition).Normal;
        if ( dir.Length < 0.001f )
            dir = Vector3.Forward;

        BallDirection = dir;
        BallSpeed = StartingBallSpeed;
        BallIsOut = true;
    }
    else
    {
        BallIsOut = false;
        BallSpeed = 0f;
    }

    SyncedPosition = WorldPosition;
}

public void ServerStopRound()
{
    if ( IsProxy ) return;

    BallIsOut = false;
    BallSpeed = 0f;
    DirBoostTimeLeft = 0f;

    PreviousTargetedPlayer = null;
    TargetedPlayer = null;

    SyncedPosition = WorldPosition;

    var rb = Components.Get<Rigidbody>();
    if ( rb != null )
    {
        rb.Velocity = Vector3.Zero;
        rb.AngularVelocity = Vector3.Zero;
    }
}


}
