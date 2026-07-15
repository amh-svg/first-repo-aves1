// Autodesk
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
// AVES
using aRib = AVES.Utilities.Ribbon_Utils;
using AVES.Extensions;

// This application belongs to the root namespace
namespace AVES
{
    // Implementing the interface for applications
    public class Application : IExternalApplication
    {
        #region Properties

        // Make a private uiCtlApp
        private static UIControlledApplication _uiCtlApp;

        #endregion

        #region On startup method

        // This will run on startup
        public Result OnStartup(UIControlledApplication uiCtlApp)
        {
            #region Globals registration

            // Store _uiCtlApp, register on idling
            _uiCtlApp = uiCtlApp;

            try
            {
                _uiCtlApp.Idling += RegisterUiApp;
            }
            catch
            {
                Globals.UiApp = null;
                Globals.UsernameRevit = null;
            }

            // Registering globals
            Globals.RegisterProperties(uiCtlApp);
            Globals.RegisterTooltips($"{Globals.AddinName}.Resources.Files.Tooltips");

            #endregion

            #region Ribbon setup

            // Add ribbon tab
            uiCtlApp.Ext_AddRibbonTab(Globals.AddinName);

            #region Family Edition Panel

            // Create Family Edition panel
            var panelFamilyEdition = uiCtlApp.Ext_AddRibbonPanel(Globals.AddinName, "Family Edition");

            // Add button to panel
            var buttonRenameFamilyNameAndType = panelFamilyEdition.Ext_AddPushButton("Rename\nFamily", "AVES.Cmds_Button6.Cmd_Button");
            var buttonDownloadFamilies = panelFamilyEdition.Ext_AddPushButton("Download\nFamilies", "AVES.Cmds_Button9.Cmd_Button");

            // Add pulldownbutton to panel
            var pulldownTest = panelFamilyEdition.Ext_AddPulldownButton("Family\nParameters", "AVES.Cmds_PullDown");

            // Add buttons to pulldown
            pulldownTest.Ext_AddPushButton("Add Shared Parameters", "AVES.Cmds_PullDown.Cmd_BatchSharedParams");
            pulldownTest.Ext_AddPushButton("Remove Parameters", "AVES.Cmds_PullDown.Cmd_BatchRemoveParams");
            pulldownTest.Ext_AddPushButton("Fill Family Parameters", "AVES.Cmds_PullDown.Cmd_CsvImporter");

            #endregion

            #region Sheet Creation Panel

            // Create Sheet Creation & Editing panel
            var panelSheets = uiCtlApp.Ext_AddRibbonPanel(Globals.AddinName, "Sheet Creation & Editing");

            // Add "Sheet Creation" pulldownbutton to panel
            var pulldown2 = panelSheets.Ext_AddPulldownButton("Create\nSheets", "AVES.Cmds_PullDownCreateSheets");

            // Add buttons to "Sheet Creation" pulldown
            pulldown2.Ext_AddPushButton("Assembly Drawings", "AVES.Cmds_PullDownCreateSheets.Cmd_AssemblyDrawings");
            pulldown2.Ext_AddPushButton("MT Drawings", "AVES.Cmds_PullDownCreateSheets.Cmd_MTDrawings");
            pulldown2.Ext_AddPushButton("Vendor Drawings", "AVES.Cmds_PullDownCreateSheets.Cmd_VendorDrawings");

            // Add "Add Dimensions" pulldownbutton to panel
            var pulldown3 = panelSheets.Ext_AddPulldownButton("Auto-add\nDimensions", "AVES.Cmds_PullDownAutoDimension");

            // Add buttons to "Add Dimensions" pulldown
            pulldown3.Ext_AddPushButton("MT Dimensions", "AVES.Cmds_PullDownAutoDimension.Cmd_MTDimensions");
            pulldown3.Ext_AddPushButton("Vendor Dimensions", "AVES.Cmds_PullDownAutoDimension.Cmd_VendorDimensions");

            // Add "Add Tags" pulldownbutton to panel
            var pulldown4 = panelSheets.Ext_AddPulldownButton("Auto-tag\nViews", "AVES.Cmds_PullDownAutoTag");

            // Add buttons to "Add Tags" pulldown
            pulldown4.Ext_AddPushButton("3D - MT Tags", "AVES.Cmds_PullDownAutoTag.Cmd_MTTag");
            pulldown4.Ext_AddPushButton("3D - Vendor Tags", "AVES.Cmds_PullDownAutoTag.Cmd_VendorTag");

            // Add button to panel
            var buttonFillSchedules = panelSheets.Ext_AddPushButton("Auto-fill\nSchedules", "AVES.Cmds_Button7.Cmd_Button");
            
            // Add Stacked button to panel
            var buttonStack = panelSheets.Ext_AddPushButtonStack(
                "Add Weight", "AVES.Cmds_Button8.Cmd_Button",
                "Fill Sheet Info", "AVES.Cmds_Button10.Cmd_Button",
                "Tag Reviewer", "AVES.Cmds_Button11.Cmd_Button");


            // Create data objects of stack
            // var stack1Data = aRib.NewPulldownButtonData("Stack 1", "AVES.Cmds_Stack1");
            // var stack2Data = aRib.NewPulldownButtonData("Stack 2", "AVES.Cmds_Stack2");
            // var stack3Data = aRib.NewPulldownButtonData("Stack 3", "AVES.Cmds_Stack3");

            // Create the stack
            // var stack = panelGeneral.AddStackedItems(stack1Data, stack2Data, stack3Data);
            // var pulldownStack1 = stack[0] as PulldownButton;
            // var pulldownStack2 = stack[1] as PulldownButton;
            // var pulldownStack3 = stack[2] as PulldownButton;

            // Add buttons to stack pulldowns
            // pulldownStack1.Ext_AddPushButton("Button", "AVES.Cmds_Stack1.Cmd_Button");
            // pulldownStack2.Ext_AddPushButton("Button", "AVES.Cmds_Stack2.Cmd_Button");
            // pulldownStack3.Ext_AddPushButton("Button", "AVES.Cmds_Stack3.Cmd_Button");

            #endregion

            #endregion

            // Final return
            return Result.Succeeded;
        }

        #endregion

        #region On shutdown method

        // This will run on shutdown
        public Result OnShutdown(UIControlledApplication uiCtlApp)
        {
            // Cleanup code logic

            // Final return
            return Result.Succeeded;
        }

        #endregion

        #region Use idling to register UiApp

        /// <summary>
        /// Registers the UiApp and Revit username globally.
        /// </summary>
        /// <param name="sender">Sender of the Idling event.</param>
        /// <param name="e">Idling event arguments.</param>
        private static void RegisterUiApp(object sender, IdlingEventArgs e)
        {
            _uiCtlApp.Idling -= RegisterUiApp;

            if (sender is UIApplication uiApp)
            {
                Globals.UiApp = uiApp;
                Globals.UsernameRevit = uiApp.Application.Username;
            }
        }

        #endregion
    }
}
