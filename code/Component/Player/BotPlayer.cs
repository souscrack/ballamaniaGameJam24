using System;
using Sandbox;

public sealed class BotPlayer : Component
{[Property]
public CapsuleCollider hitbox { get; set; }

	public void hitted()
	{
		
	 if (hitbox != null)
    {
        foreach (Collider hit in hitbox.Touching)
        {
			if ( hit.GameObject.Components.TryGet<Behavior>( out var behavior ) )
			{
				Log.Info( "Hitted propPLayer" );
				//behavior.nouvelCible();
				//dead();
				Random rand = new Random();
				Vector3 randomVector = new Vector3(
					(float)rand.NextDouble(),
					(float)rand.NextDouble(),
					(float)rand.NextDouble()
					);
					Log.Info(randomVector);
				behavior.punch(randomVector,  this.GameObject);
			}
		}
	}
	
	}

	protected override void OnFixedUpdate()
	{
		hitted();
	}


}
