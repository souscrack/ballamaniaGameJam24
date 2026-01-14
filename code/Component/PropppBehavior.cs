using Sandbox;
using System;

public sealed class PropppBehavior : Component
{

    public float BallSpeed { get; set; } = 500f;

    [Property]
    public BoxCollider hitbox { get; set; }

    // Force of the punch
    private Vector3 currentVelocity = Vector3.Zero; // Current velocity of the ball
    private bool isPunched = false; // Punch indicator
    private float punchTimer = 0f; // Timer for punch duration
    public float punchDuration = 2f; // Duration for which the ball is repelled
    private float initialPunchForce;

    protected override void OnStart()
    {
        base.OnStart();
    }

    protected override void OnUpdate()
    {
        // Debug information
        //Log.Info( $"OnUpdate - Position: {Transform.Position}, Velocity: {currentVelocity}" );
    }

    protected override void OnFixedUpdate()
    {
        if (isPunched)
        {
            // Update position based on applied force
           WorldPosition += currentVelocity * Time.Delta;

            // Decelerate the ball linearly
            float decelerationRate = initialPunchForce / punchDuration;
            float deceleration = decelerationRate * Time.Delta;
            currentVelocity -= currentVelocity.Normal * deceleration;

            // Stop the punch effect after the duration
            punchTimer += Time.Delta;
            if (punchTimer >= punchDuration)
            {
                isPunched = false;
                currentVelocity = Vector3.Zero;
            }
        }
        terrainColider();
    }

    public void punch(Vector3 direction, float PunchForce)
    {
        // Normalize the direction
        Vector3 normalizedDirection = direction.Normal;

        // Calculate velocity
        currentVelocity = normalizedDirection * PunchForce;
        initialPunchForce = PunchForce;

        // Apply the punch force in the specified direction
        isPunched = true;
        punchTimer = 0f; // Reset the punch timer

        // Debug information
        Log.Info($"Punch - Direction: {direction}, Normalized Direction: {normalizedDirection}, Velocity: {currentVelocity}");
    }

    public void terrainColider()
    {
        if (hitbox != null)
        {
            foreach (Collider hit in hitbox.Touching)
            {
                if (hit != null && hit.GameObject.Tags.Has("map"))
                {
                    // Envoyer la balle vers le haut avec sa vitesse actuelle
                    currentVelocity = new Vector3(currentVelocity.x, currentVelocity.y, Math.Abs(currentVelocity.Length));

                    // Repositionner la balle légèrement au-dessus du point de collision pour éviter de rester coincée
                   WorldPosition += Vector3.Up * (hitbox.Center + 0.1f); // Ajustez le décalage en fonction de votre hitbox

                    Log.Info("Collision with terrain detected. Adjusting position and velocity.");
                    break; // Sortir de la boucle après la première collision détectée
                }
            }
        }
    }
}
