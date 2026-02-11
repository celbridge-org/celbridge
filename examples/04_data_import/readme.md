# Data Import Example

This example demonstrates downloading data from a public REST API and importing it to a spreadsheet file.

1. Open **dublinbikes.webapp** to view the public bikes available in Dublin city. 
2. Click on a station to see how many bikes are available to hire.
3. Right click on **data_import.py** and select **Run** to run the script. Alternatively, ENTER `run "04_data_import/data_import.py"` in the console.
4. The script downloads real-time data from the **Dublin Bikes GBFS API** and saves it to a **dublinbikes.xlsx** Excel file in the same folder.
5. Open **dublinbikes.xlsx** and check that the number of available bikes matches the data presented in the webapp.

# About Dublin Bikes GBFS

[Dublin Bikes](https://www.dublinbikes.ie/) is Dublin's public bike sharing system operated by JCDecaux. The GBFS (General Bikeshare Feed Specification) is a standardized data format for bike sharing system information, providing real-time availability data.

- https://www.dublinbikes.ie/
- https://api.cyclocity.fr/contracts/dublin/gbfs/gbfs.json
- https://github.com/MobilityData/gbfs

## Data Attribution

This example uses real-time Dublin Bikes data provided by Dublin City Council via the official Dublinbikes API. The data is licensed under [Creative Commons Attribution 4.0 International (CC BY 4.0)](https://creativecommons.org/licenses/by/4.0/).

- Data Source: [Dublinbikes API DCC](https://data.gov.ie/dataset/dublinbikes-api)
- Publisher: Dublin City Council
- Operator: JCDecaux (Cyclocity)
- License: [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/)

