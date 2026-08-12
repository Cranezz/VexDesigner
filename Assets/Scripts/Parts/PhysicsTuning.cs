namespace VexDesigner.Parts
{
    using UnityEngine;

    /// <summary>
    /// Sets global physics parameters for a workshop full of small, light,
    /// precisely-shaped parts.
    ///
    /// Unity's defaults are tuned for character-scale objects moving at human
    /// speed. VEX parts are one to two orders of magnitude smaller: a quarter
    /// inch screw is about 6 mm, so at any real speed it travels many times its
    /// own length per physics step. Left on the defaults it passes through
    /// things, and a stack of them never stops trembling.
    ///
    /// Applied in code rather than through Project Settings so the reasoning
    /// lives next to the values, and so it survives a settings file being
    /// regenerated.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class PhysicsTuning : MonoBehaviour
    {
        [Tooltip("Physics steps per second. The 50 Hz default gives a 20 ms " +
                 "step, which is a long way for a small fast part to travel " +
                 "unnoticed. There are few dynamic bodies here, so a shorter " +
                 "step is affordable.")]
        [SerializeField] private int physicsRate = 100;

        [Tooltip("Position solver iterations. Higher settles stacks faster and " +
                 "with less drift; the cost is per contact, and a robot has a " +
                 "lot of resting contacts.")]
        [SerializeField] private int solverIterations = 12;

        [SerializeField] private int solverVelocityIterations = 4;

        [Tooltip("Separation allowed before the solver pushes back, in metres. " +
                 "Smaller than default because VEX tolerances are thousandths " +
                 "of an inch and visible interpenetration reads as broken.")]
        [SerializeField] private float contactOffset = 0.0008f;

        [Tooltip("Speed below which a resting body stops simulating.")]
        [SerializeField] private float sleepThreshold = 0.012f;

        private void Awake()
        {
            Time.fixedDeltaTime = 1f / physicsRate;

            Physics.defaultSolverIterations = solverIterations;
            Physics.defaultSolverVelocityIterations = solverVelocityIterations;
            Physics.defaultContactOffset = contactOffset;
            Physics.sleepThreshold = sleepThreshold;

            // Speculative contacts by default, so anything spawned without
            // going through PartFactory still gets continuous detection.
            Physics.defaultMaxDepenetrationVelocity = 1.5f;
        }
    }
}
