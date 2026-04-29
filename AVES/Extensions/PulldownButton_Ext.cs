// System
using System.Diagnostics;
// Autodesk
using Autodesk.Revit.UI;

// AVES
using aRib = AVES.Utilities.Ribbon_Utils;

namespace AVES.Extensions
{
    public static class PulldownButton_Ext
    {
        #region Button creation

        /// <summary>
        /// Attempts to create a PushButton on a PulldownButton.
        /// </summary>
        /// <param name="pulldownButton">The button to add the button to (extended).</param>
        /// <param name="buttonName">The name the user sees.</param>
        /// <param name="className">The full class name the button runs.</param>
        /// <returns>A PushButton.</returns>
        public static PushButton Ext_AddPushButton(this PulldownButton pulldownButton, string buttonName, string className)
        {
            // If no panel, return an error
            if (pulldownButton is null)
            {
                Debug.WriteLine($"ERROR: Could not add {buttonName} to pulldown.");
                return null;
            }

            // Create a data object
            var pushButtonData = aRib.NewPushButtonData(buttonName, className);

            if (pulldownButton.AddPushButton(pushButtonData) is PushButton pushButton)
            {
                // If the button was made, return it
                return pushButton;
            }
            else
            {
                // Report the error if it fails
                Debug.WriteLine($"ERROR: Could not add {buttonName} to pulldown.");
                return null;
            }
        }

        #endregion
    }
}
