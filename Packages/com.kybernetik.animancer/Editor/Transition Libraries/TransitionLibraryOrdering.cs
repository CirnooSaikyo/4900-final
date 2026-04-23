// Animancer // https://kybernetik.com.au/animancer // Copyright 2018-2026 Kybernetik //

#if UNITY_EDITOR

using Animancer.TransitionLibraries;
using System;
using UnityEngine;
using static Animancer.Editor.TransitionLibraries.TransitionLibrarySelection;

namespace Animancer.Editor.TransitionLibraries
{
    /// <summary>[Editor-Only]
    /// Operations for modifying the order of items in a <see cref="TransitionLibraryAsset"/>.
    /// </summary>
    /// https://kybernetik.com.au/animancer/api/Animancer.Editor.TransitionLibraries/TransitionLibraryOrdering
    public static class TransitionLibraryOrdering
    {
        /************************************************************************************************************************/

        /// <summary>Handles a drag and drop operation.</summary>
        public static void OnDropItem(
            this TransitionLibraryWindow window,
            object item,
            ListTargetCalculation target,
            SelectionType selectionType)
        {
            window.RecordUndo();
            window.EditorData.TransitionSortMode = TransitionSortMode.Custom;

            if (item is TransitionAssetBase transition)
                OnDropTransition(window, transition, target, selectionType);
            else if (item is TransitionGroup group)
                OnDropGroup(window, group, target);
            else
                Debug.LogWarning($"Unhandled item type: {item}");
        }

        /************************************************************************************************************************/

        /// <summary>Handles a drag and drop operation for a `transition`.</summary>
        private static void OnDropTransition(
            TransitionLibraryWindow window,
            TransitionAssetBase transition,
            ListTargetCalculation target,
            SelectionType selectionType)
        {
            var transitions = window.Data.Transitions;
            var fromTransitionIndex = Array.IndexOf(transitions, transition);

            var fromItemIndex = window.Items.IndexOf(transition);
            var fromGroup = window.Items.GetGroup(fromItemIndex);
            var fromIndexWithinGroup = int.MaxValue;
            if (fromGroup != null)
            {
                fromIndexWithinGroup = fromGroup.TransitionIndices.IndexOf(fromTransitionIndex);
                if (fromIndexWithinGroup >= 0)
                    fromGroup.TransitionIndices.RemoveAt(fromIndexWithinGroup);
            }

            var toGroup = window.Items.TryGet(target.Index, out var targetItem)
                ? window.Items.GetGroup(target.Index)
                : null;

            // If dropping onto the top half of a group, drop outside that group.
            if (target.LocalOffset < 0.5f && ReferenceEquals(toGroup, targetItem))
            {
                toGroup = null;

                if (fromItemIndex < target.Index)
                    target.Index--;
            }

            // Drop onto group or a transition in a group.
            if (toGroup != null)
            {
                var groupIndex = window.Items.IndexOf(toGroup);
                var indexWithinGroup = target.Index - groupIndex;

                // If dropping into the top half of an item, insert above that item instead of below.
                if (target.LocalOffset < 0.5f)
                    indexWithinGroup--;

                // If this item was just removed from earlier in the same list, adjust the new index.
                if (fromGroup == toGroup && fromIndexWithinGroup < indexWithinGroup)
                    indexWithinGroup--;

                indexWithinGroup = Mathf.Clamp(indexWithinGroup, 0, toGroup.TransitionIndices.Count);

                toGroup.TransitionIndices.Insert(indexWithinGroup, fromTransitionIndex);
            }
            else// Drop onto a transition with no group.
            {
                var toTransitionIndex = Array.IndexOf(transitions, targetItem);
                if (toTransitionIndex >= 0)
                {
                    // If dropping into the top half of an item, insert above that item instead of below.
                    if (target.LocalOffset >= 0.5f)
                        toTransitionIndex++;

                    // If this item was just removed from earlier in the transition list, adjust the new index.
                    if (fromTransitionIndex < toTransitionIndex)
                        toTransitionIndex--;
                }
                else if (target.Index < 0)// Above everything.
                {
                    toTransitionIndex = 0;
                }
                else// Below everything.
                {
                    toTransitionIndex = transitions.Length;
                }

                AdjustGroupIndices(window, fromItemIndex, target.Index);

                TransitionLibrarySort.MoveTransition(window, fromTransitionIndex, toTransitionIndex);
            }

            window.Selection.Select(window, targetItem, fromTransitionIndex, selectionType);
        }

        /************************************************************************************************************************/

        /// <summary>Handles a drag and drop operation for a `group`.</summary>
        private static void OnDropGroup(
            TransitionLibraryWindow window,
            TransitionGroup group,
            ListTargetCalculation target)
        {
            var fromItemIndex = window.Items.IndexOf(group);

            if (target.LocalOffset > 0.5f)
                target.Index++;

            var previousIndex = group.Index;
            AdjustGroupIndices(window, fromItemIndex, target.Index);

            if (target.Index > fromItemIndex)
                target.Index += group.Index - previousIndex;

            group.Index = window.Items.ItemToGroupIndex(target.Index);

            TransitionGroupCache.SortGroups(window.EditorData.TransitionGroups);

            window.Selection.Select(window, group, target.Index, SelectionType.Group);
        }

        /************************************************************************************************************************/

        /// <summary>Adjusts the <see cref="TransitionGroup.Index"/> for any groups an item is moved over.</summary>
        private static void AdjustGroupIndices(
            TransitionLibraryWindow window,
            int movedFromItemIndex,
            int movedToItemIndex)
        {
            var direction = Math.Sign(movedToItemIndex - movedFromItemIndex);
            movedFromItemIndex = Mathf.Clamp(movedFromItemIndex, 0, window.Items.Count - 1);
            movedToItemIndex = Mathf.Clamp(movedToItemIndex, 0, window.Items.Count - 1);
            while (true)
            {
                if (window.Items.GetItem(movedFromItemIndex) is TransitionGroup group)
                    group.Index -= direction;

                if (movedFromItemIndex == movedToItemIndex)
                    break;

                movedFromItemIndex += direction;
            }
        }

        /************************************************************************************************************************/
    }
}

#endif

