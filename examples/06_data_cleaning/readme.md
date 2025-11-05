# Data Cleaning Example

This example demonstrates using a Python script to validate and clean a messy data set.

The script runs automatically when you edit the data, so the cleaned data is kept in sync with the messy input data.

# Run Script Manually

1. Open the **messy_data.xlsx** Excel document, and notice the formatting and data entry inconsistencies.
2. Right click on **clean_data.py** and select **Run**. A **clean_data.xlsx** file is generated in the same folder as the script. 
2. Open the **clean_data.xlsx** Excel document, and notice the clean formatting.

# Run Script Automatically

1. Select the **messy_data.xlsx** document and change any number in the data. The **clean_data.py** script runs automatically.
2. Select the **clean_data.xlsx** document again and notice the new values you entered in **messy_data.xlsx** have been applied.

# Trigger a Validation Error

Ensure the **Console panel** is visible so validation error messages can be seen easily.

1. Select the **messy_data.xlsx** document and change any numeric value to an invalid value (e.g. a negative number).
2. The script runs automatically and fails validation. A descriptive error message is displayed in the console window.

# Attribution

The **messy_data.xlsx** file was created by [openAFRICA](https://open.africa/dataset/messy-data-for-data-cleaning-exercise/resource/8e4db8de-dd9e-44e3-b32f-8680974e7158) and is licensed under the [Creative Commons Attribution License (cc-by)](https://opendefinition.org/licenses/cc-by/) 