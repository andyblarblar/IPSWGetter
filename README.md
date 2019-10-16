# IPSWGetter
IPSW Getter is a simple .net worker service that uses RESTSharp to check and download new Apple firmware files in the background.
The program can be deployed as a windows service, or run from CLI. Data is saved to C:\ipsw, and only downloads when new files are found.
