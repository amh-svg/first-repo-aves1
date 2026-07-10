// System
using System.Diagnostics;
// Autodesk
using Autodesk.Revit.UI;
// AVES
using aRib = AVES.Utilities.Ribbon_Utils;

namespace AVES.Extensions
{
    public static class RibbonPanel_Ext
    {
        #region Button creation

        /// <summary>
        /// Attempts to create a PushButton on a RibbonPanel.
        /// </summary>
        /// <param name="panel">The RibbonPanel to add the button to (extended(.</param>
        /// <param name="buttonName">The name the user sees.</param>
        /// <param name="className">The full class name the button runs.</param>
        /// <returns>A PushButton.</returns>
        public static PushButton Ext_AddPushButton(this RibbonPanel panel, string buttonName, string className)
        {
            // If no panel, return an error
            if (panel is null)
            {
                Debug.WriteLine($"ERROR: Could not create {buttonName}.");
                return null;
            }

            // Create a data object
            var pushButtonData = aRib.NewPushButtonData(buttonName, className);

            if (panel.AddItem(pushButtonData) is PushButton pushButton)
            {
                // If the button was made, return it
                return pushButton;
            }
            else
            {
                // Report the error if it fails
                Debug.WriteLine($"ERROR: Could not create {buttonName}.");
                return null;
            }
        }

        /// <summary>
        /// Attempts to create a PulldownButton on a RibbonPanel.
        /// </summary>
        /// <param name="panel">The RibbonPanel to add the button to (extended).</param>
        /// <param name="buttonName">The name the user sees.</param>
        /// <param name="className">The full class name the button runs.</param>
        /// <returns>A PulldownButton.</returns>
        public static PulldownButton Ext_AddPulldownButton(this RibbonPanel panel, string buttonName, string className)
        {
            // If no panel, return an error
            if (panel is null)
            {
                Debug.WriteLine($"ERROR: Could not create {buttonName}.");
                return null;
            }

            // Create a data object
            var pulldownButtonData = aRib.NewPulldownButtonData(buttonName, className);

            if (panel.AddItem(pulldownButtonData) is PulldownButton pulldownButton)
            {
                // If the button was made, return it
                return pulldownButton;
            }
            else
            {
                // Report the error if it fails
                Debug.WriteLine($"ERROR: Could not create {buttonName}.");
                return null;
            }
        }

        /// <summary>
        /// Attempts to create a stack of 3 PushButtons on a RibbonPanel.
        /// </summary>
        /// <param name="panel">The RibbonPanel to add the buttons to (extended).</param>
        /// <param name="name1">Display text for the first button.</param>
        /// <param name="class1">Full class name for the first button's command.</param>
        /// <param name="name2">Display text for the second button.</param>
        /// <param name="class2">Full class name for the second button's command.</param>
        /// <param name="name3">Display text for the third button.</param>
        /// <param name="class3">Full class name for the third button's command.</param>
        /// <returns>An array of the 3 PushButtons, or null on failure.</returns>
        public static PushButton?[] Ext_AddPushButtonStack(this RibbonPanel panel, string name1, string class1, string name2, string class2, string name3, string class3)
        {
            if (panel is null)
            {
                Debug.WriteLine($"ERROR: Could not create stack ({name1}, {name2}, {name3}).");
                return null;
            }

            var data1 = aRib.NewPushButtonData(name1, class1);
            var data2 = aRib.NewPushButtonData(name2, class2);
            var data3 = aRib.NewPushButtonData(name3, class3);

            var stack = panel.AddStackedItems(data1, data2, data3);

            return new[]
            {
                stack[0] as PushButton,
                stack[1] as PushButton,
                stack[2] as PushButton
            };
        }

        #endregion
    }
}