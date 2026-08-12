namespace VexDesigner.Player
{
    using UnityEngine;

    /// <summary>
    /// Puts the player at the garage door every time the scene loads.
    ///
    /// Needed because a scene stores whatever position its objects were saved
    /// at, and pressing Play in the editor after nudging the player in the
    /// Scene view would otherwise start you somewhere arbitrary. Explicitly
    /// spawning makes every session begin the same way.
    ///
    /// The position is serialised rather than read from the room builder,
    /// because that builder is editor-only code and does not exist in a real
    /// build.
    /// </summary>
    [RequireComponent(typeof(FirstPersonController))]
    public sealed class PlayerSpawn : MonoBehaviour
    {
        [SerializeField] private Vector3 spawnPosition;
        [SerializeField] private float spawnYaw;

        private void Start()
        {
            GetComponent<FirstPersonController>().Teleport(spawnPosition, spawnYaw);
        }

        public void Configure(Vector3 position, float yaw)
        {
            spawnPosition = position;
            spawnYaw = yaw;
        }
    }
}
