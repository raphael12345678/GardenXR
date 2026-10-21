using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace GardenXR
{
    /// <summary>
    /// Historique des actions (pose, déplacement, suppression) avec Annuler,
    /// et suppression de l'objet tenu en main.
    /// Les boutons sont lus directement sur les manettes : aucun binding à configurer.
    /// Un seul exemplaire dans la scène (objet vide "EditHistory").
    /// </summary>
    public class GardenEditHistory : MonoBehaviour
    {
        public static GardenEditHistory Instance { get; private set; }

        public enum Hand { Left, Right }

        [Header("Boutons (lus directement sur la manette)")]
        [Tooltip("Annuler : bouton Y = main gauche + secondaryButton")]
        [SerializeField] Hand undoHand = Hand.Left;
        [SerializeField] string undoButton = "secondaryButton";
        [Tooltip("Supprimer l'objet tenu : bouton B = main droite + secondaryButton")]
        [SerializeField] Hand deleteHand = Hand.Right;
        [SerializeField] string deleteButton = "secondaryButton";

        [Header("Réglages")]
        [SerializeField] int maxSteps = 30;
        [SerializeField] bool debugLogs = true;

        [Header("Feedback")]
        [SerializeField] AudioSource audioSource;
        [SerializeField] AudioClip undoClip;
        [SerializeField] AudioClip deleteClip;

        // ---------- Étapes d'historique ----------

        abstract class Step
        {
            public abstract void Undo();
            public virtual void Discard() { }
        }

        class PlaceStep : Step
        {
            public GameObject go;
            public override void Undo() { if (go) Object.Destroy(go); }
        }

        class MoveStep : Step
        {
            public Transform target;
            public Vector3 position;
            public Quaternion rotation;
            public override void Undo() { if (target) target.SetPositionAndRotation(position, rotation); }
        }

        class DeleteStep : Step
        {
            public GameObject go;
            public override void Undo() { if (go) go.SetActive(true); }
            public override void Discard() { if (go) Object.Destroy(go); } // suppression définitive
        }

        readonly List<Step> _steps = new List<Step>();
        bool _undoWasPressed, _deleteWasPressed;

        // ---------- Cycle de vie ----------

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void Update()
        {
            if (Pressed(undoHand, undoButton, ref _undoWasPressed))
            {
                Log($"Annuler reçu – {_steps.Count} action(s) dans l'historique");
                Undo();
            }

            if (Pressed(deleteHand, deleteButton, ref _deleteWasPressed))
            {
                Log($"Supprimer reçu – objet tenu : {(PlacedItem.Held ? PlacedItem.Held.name : "aucun")}");
                if (PlacedItem.Held != null) Delete(PlacedItem.Held);
            }
        }

        /// <summary>Vrai uniquement à l'image où le bouton passe de relâché à appuyé.</summary>
        static bool Pressed(Hand hand, string controlName, ref bool wasPressed)
        {
            var device = hand == Hand.Left ? XRController.leftHand : XRController.rightHand;
            var control = device?.TryGetChildControl<ButtonControl>(controlName);
            bool isPressed = control != null && control.isPressed;

            bool down = isPressed && !wasPressed;
            wasPressed = isPressed;
            return down;
        }

        // ---------- API ----------

        public void RecordPlace(GameObject go)
        {
            Push(new PlaceStep { go = go });
            Log($"Pose enregistrée : {go.name}");
        }

        public void RecordMove(Transform t, Vector3 oldPos, Quaternion oldRot)
        {
            // Ignore les simples clics sans déplacement réel
            if ((t.position - oldPos).sqrMagnitude < 0.0001f && Quaternion.Angle(t.rotation, oldRot) < 0.5f) return;
            Push(new MoveStep { target = t, position = oldPos, rotation = oldRot });
            Log($"Déplacement enregistré : {t.name}");
        }

        public void Delete(PlacedItem item)
        {
            if (item == null) return;

            item.MarkDeleting();
            var grab = item.Grab;
            if (grab && grab.isSelected && grab.interactionManager)
                grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)grab);

            item.gameObject.SetActive(false); // masqué, pas détruit : on peut annuler
            Push(new DeleteStep { go = item.gameObject });
            Play(deleteClip);
        }

        public void Undo()
        {
            if (_steps.Count == 0) return;
            var last = _steps[_steps.Count - 1];
            _steps.RemoveAt(_steps.Count - 1);
            last.Undo();
            Play(undoClip);
        }

        // ---------- Interne ----------

        void Push(Step step)
        {
            _steps.Add(step);
            while (_steps.Count > maxSteps)
            {
                _steps[0].Discard();
                _steps.RemoveAt(0);
            }
        }

        void Play(AudioClip clip)
        {
            if (audioSource && clip) audioSource.PlayOneShot(clip);
        }

        void Log(string msg)
        {
            if (debugLogs) Debug.Log("[History] " + msg);
        }
    }
}
