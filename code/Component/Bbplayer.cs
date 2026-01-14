// Bbplayer.cs
using Sandbox;
using Sandbox.Citizen;
using System;
using Dodgeball.UI;
using NetcoManager = Dodgeball.Network.NetcoManager;
using Sandbox.UI;

public sealed class Bbplayer : Component
{
	[Property, Category( "Component" )] public GameObject Camera { get; set; }
	[Property, Category( "Component" )] public CharacterController CharacterController { get; set; }
	[Property, Category( "Component" )] public CitizenAnimationHelper AnimationHelper { get; set; }
	[Property, Category( "Component" )] public Model Citizenmodel { get; set; }
	[Property, Category( "Component" )] public ModelPhysics Ragodll { get; set; }
	[Property, Category( "Component" )] public GameObject Batte { get; set; }

	[Property] public Vector3 EyePosition { get; set; }
	[Property] public Vector3 CounterHitbox { get; set; }
	[Property] public Vector3 CranePosition { get; set; }

	[Property, Category( "Hitbox" )] public CapsuleCollider Hitboxe { get; set; }
	[Property, Category( "Hitbox" )] public SphereCollider PunchZone { get; set; }

	public Vector3 EyeWorldPostion => Transform.Local.PointToWorld( EyePosition );

	[Property, Category( "Stats" ), Range( 0f, 400f )] public float Walkspeed { get; set; } = 200f;
	[Property, Category( "Stats" ), Range( 0f, 800f )] public float Runspeed { get; set; } = 3500f;
	[Property, Category( "Stats" ), Range( 0f, 800f )] public float JumpStrength { get; set; } = 400f;

	[Property, Category( "Stats" ), Range( 0f, 1000f )] public float PunchStrength { get; set; } = 1f;
	[Property, Category( "Stats" ), Range( 0f, 5f)] public float PunchColdown { get; set; } = 0.5f;
	[Property, Category( "Stats" ), Range( 0f, 200f)] public float PunchRange { get; set; } = 50f;
	[Property, Category( "Stats" ), Range( 0f, 1000f)] public float DashRange { get; set; } = 500f;
	public bool AvaibleDoubleJump = false;
	public Angles EyeAngles { get; set; }

	private Transform _initialCameraTransform;
	private TimeSince _lastPunch;
	private bool isRagdolled = false;

	// ─────────────────────────────────────────────────────────────
	// RPC VISUELS JOUEUR (OK de rester ici)
	// ─────────────────────────────────────────────────────────────

	[Rpc.Broadcast]
	private void RpcPlayPunchAnim()
	{
		if ( AnimationHelper == null ) return;

		AnimationHelper.HoldType = CitizenAnimationHelper.HoldTypes.Punch;
		AnimationHelper.Target.Set( "b_attack", true );
		_lastPunch = 0;
	}

	[Rpc.Broadcast]
	private void RpcSetRagdoll( bool enable )
	{
		isRagdolled = enable;
		ApplyRagdollState();
	}

	private void ApplyRagdollState()
	{
		if ( Ragodll != null ) Ragodll.Enabled = isRagdolled;

		// batte collider off en ragdoll
		if ( Batte != null )
		{
			var batCol = Batte.Components.Get<CapsuleCollider>();
			if ( batCol != null ) batCol.Enabled = !isRagdolled;
		}
	}

	protected override void OnStart()
	{
		// Caméra uniquement pour le joueur local
		if ( Camera != null )
		{
			_initialCameraTransform = Camera.Transform.Local;
			if ( IsProxy ) Camera.Enabled = false;
		}

		if ( IsProxy )
			{
				var hud = GameObject.GetComponentInChildren<GameHud>( includeDisabled: true, includeSelf: true );
				if ( hud != null ) hud.Enabled = false;

				var screen = GameObject.GetComponentInChildren<ScreenPanel>( includeDisabled: true, includeSelf: true );
				if ( screen != null ) screen.Enabled = false;
			}

		// HUD / vêtements seulement local
		if ( !IsProxy )
		{
			if ( GameHud.Instance == null )
			{
				Log.Info( "ROOT HUD ATTACHED" );
			}

			// vêtements seulement local user
			if ( Components.TryGet<SkinnedModelRenderer>( out var smr ) )
			{
				var clothing = ClothingContainer.CreateFromLocalUser();
				clothing.Apply( smr );
			}
		}

		ApplyRagdollState();
	}

	protected override void OnUpdate()
	{
		// PROXY: pas d'input, pas de caméra
		if ( IsProxy ) return;

		EyeAngles += Input.AnalogLook;
		EyeAngles = EyeAngles.WithPitch( MathX.Clamp( EyeAngles.pitch, -100f, 50f ) );
		Transform.Rotation = Rotation.FromYaw( EyeAngles.yaw );

		UpdateLocalCamera();

		if ( Input.Pressed( "ragdoll" ) )
		{
			isRagdolled = !isRagdolled;
			ApplyRagdollState();
			RpcSetRagdoll( isRagdolled );
		}
	}

	private void UpdateLocalCamera()
	{
		if ( Camera == null ) return;

		var cameraTransform = _initialCameraTransform.RotateAround( EyePosition, EyeAngles.WithYaw( 0f ) );
		var cameraPosition = Transform.Local.PointToWorld( cameraTransform.Position );

		var cameraTrace = Scene.Trace.Ray( EyeWorldPostion, cameraPosition )
			.Size( 5f )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "player" )
			.Run();

		Camera.Transform.Position = cameraTrace.EndPosition;
		Camera.Transform.LocalRotation = cameraTransform.Rotation;
	}

	protected override void OnFixedUpdate()
	{
		if ( CharacterController == null ) return;

		// Proxy : pas d'input, mais anim OK
		if ( IsProxy )
		{
			UpdateAnimations();
			return;
		}

		if ( CharacterController.IsOnGround )
		{
			var wishSpeed = Input.Down( "Run" ) ? Runspeed : Walkspeed;
			var wishvelocity = Input.AnalogMove.Normal * wishSpeed * Transform.Rotation;
			CharacterController.Accelerate( wishvelocity );
		}

		jumpMethod();
		CharacterController.Move();

		UpdateAnimations();

		if ( Input.Pressed( "Punch" ) && _lastPunch >= PunchColdown )
		{
			Punch();
		}

		if ( Input.Pressed( "dash" ) )
		{
			//dash();
			dead();
		}

		doesHit();
	}

	private void UpdateAnimations()
	{
		if ( AnimationHelper == null ) return;

		AnimationHelper.IsGrounded = CharacterController.IsOnGround;
		AnimationHelper.WithVelocity( CharacterController.Velocity );
		if ( _lastPunch >= 5f ) AnimationHelper.HoldType = CitizenAnimationHelper.HoldTypes.None;
	}

	public bool doesHit()
	{
		if ( Hitboxe != null )
		{
			foreach ( Collider hit in Hitboxe.Touching )
			{
				if ( hit.GameObject.Components.TryGet<Behavior>( out var behavior ) )
				{
					Log.Info( "Hitted" );
					dead();
					return true;
				}
			}
		}
		return false;
	}

	public void dash()
	{
		var start = EyeWorldPostion;
		var direction = EyeAngles.Forward;
		var end = start + (direction * DashRange);

		var dashTrace = Scene.Trace
			.FromTo( EyeWorldPostion, EyeWorldPostion + EyeAngles.Forward * DashRange )
			.Size( 100f )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();

		if ( dashTrace.Hit )
			LocalPosition = dashTrace.HitPosition;
		else
			LocalPosition = end;
	}

	public void jumpMethod()
	{
		if ( CharacterController.IsOnGround )
		{
			AvaibleDoubleJump = true;
			CharacterController.Acceleration = 20f;
			CharacterController.ApplyFriction( 10f, 20f );

			if ( Input.Down( "Jump" ) )
			{
				CharacterController.Punch( Vector3.Up * JumpStrength );
				if ( AnimationHelper != null ) AnimationHelper.TriggerJump();
			}
		}
		else
		{
			CharacterController.Acceleration = 3f;
			CharacterController.Velocity += Scene.PhysicsWorld.Gravity * Time.Delta;

			if ( Input.Pressed( "Jump" ) && AvaibleDoubleJump )
			{
				AvaibleDoubleJump = false;
				CharacterController.Velocity = CharacterController.Velocity.WithZ( JumpStrength );
				if ( AnimationHelper != null ) AnimationHelper.TriggerJump();
			}
		}
	}

	public void Punch()
	{
		// feedback local immédiat
		if ( AnimationHelper != null )
		{
			AnimationHelper.HoldType = CitizenAnimationHelper.HoldTypes.Punch;
			AnimationHelper.Target.Set( "b_attack", true );
		}

		// réplication anim pour les autres
		RpcPlayPunchAnim();

		if ( PunchZone == null ) return;
		if ( Camera == null )
		{
			Log.Warning( "Camera non assignée, punch annulé !" );
			return;
		}

		Vector3 punchDirection = Camera.Transform.Rotation.Forward;
		foreach ( Collider hit in PunchZone.Touching )
		{
			if ( hit.GameObject.Components.TryGet<Behavior>( out var behavior ) )
			{
				Log.Info( "punched" );
				behavior.punch( punchDirection, this.GameObject );
			}
			if ( hit.GameObject.Components.TryGet<PropppBehavior>( out var propbehavior ) )
			{
				propbehavior.punch( punchDirection, PunchStrength );
			}
		}
		_lastPunch = 0;
	}
public void dead()
{
	Log.Info( "MORT" );

	var owner = GameObject.Network.OwnerConnection ?? Connection.Local;
var id = owner.Id.ToString("N");
NetcoManager.RpcSetDeadState( id, true );

	isRagdolled = true;
	ApplyRagdollState();
	RpcSetRagdoll( true );
}

public void ServerRespawnAt( Vector3 pos, Rotation rot )
{
	// Appelé par le serveur -> ordonne au owner de se replacer
	RpcRespawn( pos, rot );
}



[Rpc.Owner]
private void RpcRespawn( Vector3 pos, Rotation rot )
{
	// replace le joueur
	Transform.Position = pos;
	Transform.Rotation = rot;

	// enlève ragdoll / état mort
	isRagdolled = false;
	ApplyRagdollState();
	RpcSetRagdoll( false );

	// si tu veux forcer le contrôleur à se recalculer
	if ( CharacterController != null )
	{
		CharacterController.Velocity = Vector3.Zero;
	}
}



}
