from datetime import datetime, timedelta
import pandas as pd
from pathlib import Path

OUTPUT_PATH = "/temp-output-directory/option/usa/universes/aapl"

df = pd.DataFrame()
for i, file in enumerate(Path.glob(Path.cwd(), "*.csv")):
    if i == 0:
        continue
    try:
        df_daily = pd.read_csv(file, usecols=[14, 15, 16])
        series = df_daily.iloc[-1]
        daily_entry = series.to_frame().T
        daily_entry.index = [datetime.strptime(file.stem, "%Y%m%d") + timedelta(1)]
        df = pd.concat([df, daily_entry])
    except:
        pass

### Test comparison generation
### Adapted from https://s3.amazonaws.com/tastytradepublicmedia/website/cms/SKINNY_ivr_and_ivp.txt/original/SKINNY_ivr_and_ivp.txt?_sp=50d826c5-0948-4c48-9851-02767cd310a9.1721559732707
### Michael Rechenthin, Ph.D., tastytrade/dough Research Team
# Extract IV column
history = df[["iv_30"]]

# calculate the IV rank
# ---------------------------
# calculate the IV rank
low_over_timespan = history.rolling(252).min()
high_over_timespan = history.rolling(252).max()
iv_rank = (history - low_over_timespan) / (high_over_timespan - low_over_timespan) * 100

# calculate the IV percentile
# ---------------------------
# how many times over the past year, has IV been below the current IV
def below(df1):
  count = 0
  for i in range(df1.shape[0]):
      if df1[-1] > df1[i]:
          count += 1
  return count
count_below = history.rolling(252).apply(below)
iv_percentile = count_below / 252 * 100

df = pd.concat([df, iv_rank, iv_percentile], axis=1)
df.columns = list(df.columns)[:3] + ["test_iv_rank", "test_iv_percentile"]

df.dropna().to_csv("generated_samples.csv")