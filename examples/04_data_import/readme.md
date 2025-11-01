# Data Import Example

This example demonstrates downloading data from a public REST API and importing it to a spreadsheet file.

1. Open **citybikes.webapp** to view the public bikes available in Dublin city. 
2. Click on a station to see how many bikes are available to hire.
3. Right click on **data_import.py** and select **Run** to run the script. Alternatively, ENTER `run "04_data_import/data_import.py"` in the console.
4. The script downloads data from the public **Citybikes REST API** and saves it to a **citybikes.xlsx** Excel file in the same folder.
5. Open **citybikes.xlsx** and check that the number of available bikes matches the data presented in the webapp.

# About Citybikes

[Citybikes](https://citybik.es/) provides real-time public bike available data for more than 400 cities and the Citybikes API is the most widely used dataset for building bike sharing transportation projects.

- https://citybik.es/
- https://api.citybik.es/v2/